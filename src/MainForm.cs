using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KlembordTool
{
    public class MainForm : Form
    {
        const int HK_MAIN = 1;
        const int HK_FAV_BASE = 1000;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "KlembordTool";
        const string Hint = "Klik = plakken in vorig venster  ·  Sleep naar Bewaarlijst of Reeks  ·  Rechtermuisknop = meer opties";

        static readonly HashSet<string> IgnoredClasses = new HashSet<string>
        {
            "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
            "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "ForegroundStaging", "MultitaskingViewFrame",
            "TaskListThumbnailWnd", "Progman", "WorkerW", "#32768"
        };

        readonly AppData data;
        readonly uint myPid;
        readonly int showMsg;
        Icon appIcon;
        NotifyIcon tray;
        ClipList histList, favList, seqList;
        TextBox searchBox;
        Label seqStatus;
        Button seqStartBtn;
        CheckBox loopCheck;
        ToolStripStatusLabel statusLabel;
        ToolStripMenuItem miTop, miHide, miSaveHist, miStartup, miTrayStop;
        readonly Timer saveTimer, advanceTimer, statusTimer;

        bool allowShow, exiting, balloonShown, mainHotkeyFailed;
        IntPtr lastTarget;
        Native.WinEventDelegate winEventProc;
        IntPtr winEventHook;
        Native.LowLevelKeyboardProc kbProc;
        IntPtr kbHook;
        string ignoreText;
        bool seqActive, ctrlDown, vPending;
        int seqIndex, suppressUntil;
        readonly Dictionary<int, Clip> hotkeyMap = new Dictionary<int, Clip>();
        readonly HashSet<Clip> failedHotkeys = new HashSet<Clip>();

        public MainForm(bool startHidden)
        {
            allowShow = !startHidden;
            myPid = (uint)Process.GetCurrentProcess().Id;
            showMsg = Native.RegisterWindowMessage(Program.ShowMessageName);
            data = AppData.Load();

            saveTimer = new Timer { Interval = 1000 };
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveNow(); };
            advanceTimer = new Timer { Interval = 150 };
            advanceTimer.Tick += delegate { advanceTimer.Stop(); AdvanceSequence(); };
            statusTimer = new Timer { Interval = 5000 };
            statusTimer.Tick += delegate { statusTimer.Stop(); statusLabel.Text = Hint; };

            BuildUi();
            BuildTray();
            ApplyOptions();
            RestoreWindowBounds();
            RefreshAll();
        }

        // ------------------------------------------------------------------ UI

        void BuildUi()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Klembord Tool";
            Font = new Font("Segoe UI", 9.75F);
            BackColor = Theme.Bg;
            appIcon = Theme.MakeIcon();
            Icon = appIcon;
            ClientSize = new Size(1000, 580);
            MinimumSize = new Size(700, 380);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            // Menu
            var menu = new MenuStrip { BackColor = Theme.Bg };
            var mFile = new ToolStripMenuItem("&Bestand");
            mFile.DropDownItems.Add("Sneltoets voor dit venster…", null, delegate { SetMainHotkey(); });
            mFile.DropDownItems.Add("Gegevensmap openen", null, delegate
            {
                Directory.CreateDirectory(AppData.Folder);
                Process.Start("explorer.exe", "\"" + AppData.Folder + "\"");
            });
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add("Afsluiten", null, delegate { ExitApp(); });

            var mOpt = new ToolStripMenuItem("&Opties");
            miTop = new ToolStripMenuItem("Altijd bovenop") { CheckOnClick = true };
            miTop.CheckedChanged += delegate { data.AlwaysOnTop = miTop.Checked; TopMost = miTop.Checked; ScheduleSave(); };
            miHide = new ToolStripMenuItem("Venster verbergen na plakken") { CheckOnClick = true };
            miHide.CheckedChanged += delegate { data.HideAfterPaste = miHide.Checked; ScheduleSave(); };
            miSaveHist = new ToolStripMenuItem("Geschiedenis bewaren na afsluiten") { CheckOnClick = true };
            miSaveHist.CheckedChanged += delegate { data.SaveHistory = miSaveHist.Checked; ScheduleSave(); };
            var mMax = new ToolStripMenuItem("Maximaal aantal in geschiedenis");
            foreach (int n in new[] { 50, 100, 200, 500, 1000 })
            {
                int count = n;
                var it = new ToolStripMenuItem(n + " items") { Tag = n };
                it.Click += delegate { data.MaxHistory = count; TrimHistory(); RefreshHistory(); ScheduleSave(); };
                mMax.DropDownItems.Add(it);
            }
            mMax.DropDownOpening += delegate
            {
                foreach (ToolStripMenuItem it in mMax.DropDownItems) it.Checked = (int)it.Tag == data.MaxHistory;
            };
            miStartup = new ToolStripMenuItem("Starten met Windows");
            miStartup.Click += delegate { SetStartup(!StartupEnabled()); };
            mOpt.DropDownOpening += delegate { miStartup.Checked = StartupEnabled(); };
            mOpt.DropDownItems.AddRange(new ToolStripItem[] { miTop, miHide, miSaveHist, mMax, new ToolStripSeparator(), miStartup });

            var mHelp = new ToolStripMenuItem("&Help");
            mHelp.DropDownItems.Add("Uitleg", null, delegate { ShowHelp(); });
            menu.Items.AddRange(new ToolStripItem[] { mFile, mOpt, mHelp });

            // Status bar
            var status = new StatusStrip { BackColor = Theme.Bg, SizingGrip = true };
            statusLabel = new ToolStripStatusLabel(Hint) { ForeColor = Theme.Sub };
            status.Items.Add(statusLabel);

            // History column
            histList = new ClipList { BackColor = Theme.Card, Dock = DockStyle.Fill, EmptyText = "Nog niets gekopieerd.\nKnip of kopieer tekst (Ctrl+X / Ctrl+C) en het verschijnt hier." };
            histList.Meta = (c, i) => HistoryMeta(c);
            histList.ItemClicked += c => PasteClip(c);
            searchBox = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
            searchBox.HandleCreated += delegate { Native.SendMessage(searchBox.Handle, Native.EM_SETCUEBANNER, (IntPtr)1, "Zoeken…  (Ctrl+F)"); };
            searchBox.TextChanged += delegate { RefreshHistory(); };
            searchBox.KeyDown += SearchKeyDown;
            histList.KeyDown += (s, e) => ListKeyDown(histList, e);
            histList.KeyPress += (s, e) =>
            {
                if (!char.IsControl(e.KeyChar))
                {
                    searchBox.Focus();
                    searchBox.AppendText(e.KeyChar.ToString());
                    e.Handled = true;
                }
            };
            histList.ContextMenuStrip = BuildHistoryMenu();
            var histCol = MakeColumn("Geschiedenis", searchBox, histList,
                MakeButton("→ Bewaarlijst", delegate { AddTo(favList, Sel(histList)); }),
                MakeButton("→ Reeks", delegate { AddTo(seqList, Sel(histList)); }),
                MakeButton("Wissen", delegate { ClearHistory(); }));

            // Favorites column
            favList = new ClipList { BackColor = Theme.Card, Dock = DockStyle.Fill, AcceptDrops = true, AllowReorder = true, EmptyText = "Sleep regels uit de geschiedenis hierheen om ze te bewaren." };
            favList.Meta = (c, i) => FavoriteMeta(c);
            favList.IsWarning = c => failedHotkeys.Contains(c);
            favList.ItemClicked += c => PasteClip(c);
            favList.ItemDropped += (d, idx, move) => OnDropped(favList, d, idx, move);
            favList.KeyDown += (s, e) => ListKeyDown(favList, e);
            favList.DoubleClick += delegate { EditClip(favList, Sel(favList)); };
            favList.ContextMenuStrip = BuildFavoriteMenu();
            var favHint = new Label { Text = "Vaste knipsels. Koppel een sneltoets om ze overal direct te plakken.", AutoSize = true, ForeColor = Theme.Sub, Margin = new Padding(0, 0, 0, 6) };
            var favCol = MakeColumn("Bewaarlijst", favHint, favList,
                MakeButton("Nieuw…", delegate { NewFavorite(); }),
                MakeButton("Sneltoets…", delegate { SetClipHotkey(Sel(favList)); }),
                MakeButton("Verwijderen", delegate { RemoveClip(favList, Sel(favList)); }));

            // Sequence column
            seqList = new ClipList { BackColor = Theme.Card, Dock = DockStyle.Fill, AcceptDrops = true, AllowReorder = true, EmptyText = "Sleep regels hierheen of gebruik 'Regels toevoegen…'. Klik een regel om te plakken; elke volgende Ctrl+V plakt de volgende regel." };
            seqList.Meta = (c, i) => "#" + (i + 1) + (seqActive && i == seqIndex ? "   ▶ volgende bij Ctrl+V" : "");
            seqList.IsMarked = i => seqActive && i == seqIndex;
            seqList.ItemClicked += c => StartSequence(data.Sequence.IndexOf(c), true);
            seqList.ItemDropped += (d, idx, move) => OnDropped(seqList, d, idx, move);
            seqList.KeyDown += (s, e) => ListKeyDown(seqList, e);
            seqList.DoubleClick += delegate { EditClip(seqList, Sel(seqList)); };
            seqList.ContextMenuStrip = BuildSequenceMenu();
            seqStatus = new Label { AutoSize = true, ForeColor = Theme.Sub, Margin = new Padding(0, 0, 0, 6) };
            seqStartBtn = MakeButton("▶ Start reeks", delegate { if (seqActive) StopSequence(); else StartSequence(Math.Max(0, seqList.SelectedIndex), false); });
            loopCheck = new CheckBox { Text = "Herhalen", AutoSize = true, Margin = new Padding(6, 6, 2, 2) };
            loopCheck.CheckedChanged += delegate { data.LoopSequence = loopCheck.Checked; ScheduleSave(); };
            var seqCol = MakeColumn("Reekslijst", seqStatus, seqList,
                seqStartBtn,
                MakeButton("Regels toevoegen…", delegate { AddLinesToSequence(); }),
                MakeButton("Wissen", delegate { ClearSequence(); }),
                loopCheck);

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(6) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.Controls.Add(histCol, 0, 0);
            grid.Controls.Add(favCol, 1, 0);
            grid.Controls.Add(seqCol, 2, 0);

            Controls.Add(grid);
            Controls.Add(status);
            Controls.Add(menu);
            MainMenuStrip = menu;
            ResumeLayout(false);
            PerformLayout();
        }

        Control MakeColumn(string title, Control top, ClipList list, params Control[] buttons)
        {
            var card = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Card,
                Margin = new Padding(5), Padding = new Padding(10, 8, 10, 8)
            };
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(new Label
            {
                Text = title, AutoSize = true, Font = new Font("Segoe UI Semibold", 12F), ForeColor = Theme.Text,
                Margin = new Padding(0, 0, 0, 6)
            }, 0, 0);
            card.Controls.Add(top, 0, 1);
            card.Controls.Add(list, 0, 2);
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 6, 0, 0) };
            flow.Controls.AddRange(buttons);
            card.Controls.Add(flow, 0, 3);
            return card;
        }

        static Button MakeButton(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.System, Margin = new Padding(0, 0, 4, 0) };
            b.Click += click;
            return b;
        }

        ContextMenuStrip BuildHistoryMenu()
        {
            var cm = new ContextMenuStrip();
            cm.Items.Add("Plakken", null, delegate { PasteClip(Sel(histList)); });
            cm.Items.Add("Alleen kopiëren", null, delegate { CopyOnly(Sel(histList)); });
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add("Naar bewaarlijst", null, delegate { AddTo(favList, Sel(histList)); });
            cm.Items.Add("Naar reekslijst", null, delegate { AddTo(seqList, Sel(histList)); });
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add("Verwijderen", null, delegate { RemoveClip(histList, Sel(histList)); });
            cm.Items.Add(new ToolStripMenuItem("Geschiedenis wissen…", null, delegate { ClearHistory(); }) { Tag = "always" });
            EnableBySelection(cm, histList);
            return cm;
        }

        ContextMenuStrip BuildFavoriteMenu()
        {
            var cm = new ContextMenuStrip();
            cm.Items.Add("Plakken", null, delegate { PasteClip(Sel(favList)); });
            cm.Items.Add("Alleen kopiëren", null, delegate { CopyOnly(Sel(favList)); });
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add("Sneltoets instellen…", null, delegate { SetClipHotkey(Sel(favList)); });
            cm.Items.Add("Bewerken…", null, delegate { EditClip(favList, Sel(favList)); });
            cm.Items.Add("Naar reekslijst", null, delegate { AddTo(seqList, Sel(favList)); });
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add("Verwijderen", null, delegate { RemoveClip(favList, Sel(favList)); });
            cm.Items.Add(new ToolStripMenuItem("Nieuw knipsel…", null, delegate { NewFavorite(); }) { Tag = "always" });
            EnableBySelection(cm, favList);
            return cm;
        }

        ContextMenuStrip BuildSequenceMenu()
        {
            var cm = new ContextMenuStrip();
            cm.Items.Add("Plakken en reeks vanaf hier starten", null, delegate { StartSequence(seqList.SelectedIndex, true); });
            cm.Items.Add("Reeks vanaf hier klaarzetten (zelf Ctrl+V)", null, delegate { StartSequence(seqList.SelectedIndex, false); });
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add("Bewerken…", null, delegate { EditClip(seqList, Sel(seqList)); });
            cm.Items.Add("Verwijderen", null, delegate { RemoveClip(seqList, Sel(seqList)); });
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add(new ToolStripMenuItem("Regels toevoegen…", null, delegate { AddLinesToSequence(); }) { Tag = "always" });
            cm.Items.Add(new ToolStripMenuItem("Reeks stoppen", null, delegate { StopSequence(); }) { Tag = "seq" });
            cm.Items.Add(new ToolStripMenuItem("Lijst wissen…", null, delegate { ClearSequence(); }) { Tag = "always" });
            EnableBySelection(cm, seqList);
            return cm;
        }

        void EnableBySelection(ContextMenuStrip cm, ClipList list)
        {
            cm.Opening += delegate
            {
                bool has = list.SelectedItem != null;
                foreach (ToolStripItem it in cm.Items)
                {
                    string tag = it.Tag as string;
                    it.Enabled = tag == "always" || (tag == "seq" ? seqActive : has);
                }
            };
        }

        void BuildTray()
        {
            var cm = new ContextMenuStrip();
            cm.Items.Add("Openen", null, delegate { ShowMain(); });
            miTrayStop = new ToolStripMenuItem("Reeks stoppen", null, delegate { StopSequence(); });
            cm.Items.Add(miTrayStop);
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add("Afsluiten", null, delegate { ExitApp(); });
            cm.Opening += delegate { miTrayStop.Enabled = seqActive; };
            tray = new NotifyIcon { Icon = appIcon, Text = "Klembord Tool", Visible = true, ContextMenuStrip = cm };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ToggleWindow(); };
        }

        void ApplyOptions()
        {
            miTop.Checked = data.AlwaysOnTop;
            TopMost = data.AlwaysOnTop;
            miHide.Checked = data.HideAfterPaste;
            miSaveHist.Checked = data.SaveHistory;
            loopCheck.Checked = data.LoopSequence;
        }

        void RestoreWindowBounds()
        {
            if (data.WinW <= 0 || data.WinH <= 0 || data.WinX == int.MinValue) return;
            var r = new Rectangle(data.WinX, data.WinY, data.WinW, data.WinH);
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(r)))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = r;
            }
        }

        void ShowStatus(string text)
        {
            statusLabel.Text = text;
            statusTimer.Stop();
            statusTimer.Start();
        }

        // ------------------------------------------------------------------ Refresh

        static Clip Sel(ClipList l) { return l.SelectedItem as Clip; }

        List<Clip> ListOf(ClipList l)
        {
            if (l == favList) return data.Favorites;
            if (l == seqList) return data.Sequence;
            return data.History;
        }

        static void Fill(ClipList l, IEnumerable<Clip> items)
        {
            object sel = l.SelectedItem;
            int top = l.TopIndex;
            l.BeginUpdate();
            l.Items.Clear();
            foreach (var c in items) l.Items.Add(c);
            if (sel != null && l.Items.Contains(sel)) l.SelectedItem = sel;
            if (top < l.Items.Count) l.TopIndex = top;
            l.EndUpdate();
            l.Invalidate();
        }

        void RefreshHistory()
        {
            string q = searchBox.Text.Trim();
            IEnumerable<Clip> items = data.History;
            if (q.Length > 0) items = items.Where(c => c.Text.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);
            Fill(histList, items);
        }

        void RefreshFavorites() { Fill(favList, data.Favorites); }

        void RefreshSequence()
        {
            Fill(seqList, data.Sequence);
            UpdateSequenceStatus();
        }

        void RefreshAll()
        {
            RefreshHistory();
            RefreshFavorites();
            RefreshSequence();
        }

        void UpdateSequenceStatus()
        {
            if (seqActive)
            {
                seqStatus.Text = "Actief: regel " + (seqIndex + 1) + " van " + data.Sequence.Count + " staat klaar voor Ctrl+V";
                seqStatus.ForeColor = Theme.Accent;
                seqStartBtn.Text = "■ Stop reeks";
                tray.Text = "Klembord Tool – reeks " + (seqIndex + 1) + "/" + data.Sequence.Count;
            }
            else
            {
                seqStatus.Text = data.Sequence.Count == 0 ? "Plak regels één voor één met Ctrl+V." : data.Sequence.Count + " regels · klik een regel om te beginnen";
                seqStatus.ForeColor = Theme.Sub;
                seqStartBtn.Text = "▶ Start reeks";
                tray.Text = "Klembord Tool";
            }
        }

        static string LinesInfo(string text)
        {
            int lines = text.Split('\n').Length;
            string s = "";
            if (lines > 1) s += " · " + lines + " regels";
            if (text.Length > 80) s += " · " + text.Length + " tekens";
            return s;
        }

        static string HistoryMeta(Clip c)
        {
            var nl = new CultureInfo("nl-NL");
            string when = c.Time.Date == DateTime.Today ? c.Time.ToString("HH:mm", nl) : c.Time.ToString("d MMM HH:mm", nl);
            return when + LinesInfo(c.Text);
        }

        string FavoriteMeta(Clip c)
        {
            string hk = c.Key != 0 ? "⌨ " + Clip.HotkeyText(c.Mods, c.Key) : "geen sneltoets";
            if (failedHotkeys.Contains(c)) hk += "  (al in gebruik door ander programma!)";
            return hk + LinesInfo(c.Text);
        }

        void ScheduleSave()
        {
            saveTimer.Stop();
            saveTimer.Start();
        }

        void SaveNow()
        {
            if (WindowState == FormWindowState.Normal && Visible)
            {
                data.WinX = Left; data.WinY = Top; data.WinW = Width; data.WinH = Height;
            }
            data.Save();
        }

        // ------------------------------------------------------------------ Window / messages

        protected override void SetVisibleCore(bool value)
        {
            if (!allowShow)
            {
                value = false;
                if (!IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.AddClipboardFormatListener(Handle);
            RegisterAllHotkeys();
            if (winEventHook == IntPtr.Zero)
            {
                winEventProc = OnWinEvent;
                winEventHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero, winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            Native.RemoveClipboardFormatListener(Handle);
            UnregisterAllHotkeys();
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_CLIPBOARDUPDATE) OnClipboardUpdate();
            else if (m.Msg == Native.WM_HOTKEY) OnHotkey(m.WParam.ToInt32());
            else if (m.Msg == showMsg && showMsg != 0) ShowMain();
            base.WndProc(ref m);
        }

        void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (hwnd == IntPtr.Zero) return;
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            if (pid == myPid) return;
            if (IgnoredClasses.Contains(Native.GetClassNameOf(hwnd))) return;
            lastTarget = hwnd;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                if (searchBox.Text.Length > 0) searchBox.Clear();
                else Hide();
                return true;
            }
            if (keyData == (Keys.Control | Keys.F))
            {
                searchBox.Focus();
                searchBox.SelectAll();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void SearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down && histList.Items.Count > 0)
            {
                histList.Focus();
                histList.SelectedIndex = 0;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter && histList.Items.Count > 0)
            {
                PasteClip((Clip)histList.Items[0]);
                e.Handled = e.SuppressKeyPress = true;
            }
        }

        void ListKeyDown(ClipList list, KeyEventArgs e)
        {
            Clip c = Sel(list);
            if (c == null) return;
            if (e.KeyCode == Keys.Enter)
            {
                if (list == seqList) StartSequence(list.SelectedIndex, true);
                else PasteClip(c);
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Delete)
            {
                RemoveClip(list, c);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F2 && list != histList)
            {
                EditClip(list, c);
                e.Handled = true;
            }
        }

        void ShowMain()
        {
            allowShow = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            if (histList.Items.Count > 0)
            {
                histList.Focus();
                histList.SelectedIndex = 0;
            }
            else searchBox.Focus();
        }

        void ToggleWindow()
        {
            if (Visible && WindowState != FormWindowState.Minimized && Native.GetForegroundWindow() == Handle) Hide();
            else ShowMain();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                SaveNow();
                Hide();
                if (!balloonShown)
                {
                    balloonShown = true;
                    tray.ShowBalloonTip(3000, "Klembord Tool draait nog",
                        "Het programma blijft het klembord bijhouden in het systeemvak. Afsluiten kan via het pictogram.", ToolTipIcon.Info);
                }
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            RemoveKeyboardHook();
            if (winEventHook != IntPtr.Zero) Native.UnhookWinEvent(winEventHook);
            winEventHook = IntPtr.Zero;
            SaveNow();
            tray.Visible = false;
            tray.Dispose();
            base.OnFormClosed(e);
        }

        void ExitApp()
        {
            exiting = true;
            SaveNow();
            Close();
        }

        void ShowHelp()
        {
            string main = data.MainKey != 0 ? Clip.HotkeyText(data.MainMods, data.MainKey) : "(geen)";
            MessageBox.Show(this,
                "GESCHIEDENIS\n" +
                "Alles wat je knipt of kopieert (tekst) komt bovenaan de lijst. Klik op een regel en hij wordt " +
                "direct geplakt in het programma waar je net was. Typ om te zoeken.\n\n" +
                "BEWAARLIJST\n" +
                "Sleep regels hierheen voor dingen die je vaker nodig hebt. Met rechtermuisknop › Sneltoets " +
                "koppel je een toetscombinatie (bijv. Ctrl+Alt+1). Die combinatie plakt het knipsel in elk programma.\n\n" +
                "REEKSLIJST\n" +
                "Zet regels in de juiste volgorde (slepen of 'Regels toevoegen…'). Klik op een regel: die wordt geplakt " +
                "en de volgende staat meteen klaar. Elke keer dat je Ctrl+V drukt, komt de volgende regel. " +
                "Of druk op '▶ Start reeks' en plak zelf met Ctrl+V.\n\n" +
                "SNELTOETSEN\n" +
                main + " = dit venster openen/verbergen (instelbaar via Bestand).\n" +
                "Enter = plakken, Delete = verwijderen, F2 = bewerken, Esc = verbergen.\n\n" +
                "Sluiten (X) verbergt het venster naar het systeemvak. Echt afsluiten: Bestand › Afsluiten.",
                "Klembord Tool – uitleg", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ------------------------------------------------------------------ Clipboard

        void OnClipboardUpdate()
        {
            string text = ReadClipboardText();
            if (text == null) return;
            if (ignoreText != null && text == ignoreText) return;
            ignoreText = null;
            if (text.Trim().Length == 0) return;
            AddHistory(text);
        }

        static string ReadClipboardText()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    IDataObject d = Clipboard.GetDataObject();
                    if (d == null) return null;
                    // Password managers mark secrets with these formats; respect them.
                    if (d.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing")) return null;
                    if (d.GetDataPresent("CanIncludeInClipboardHistory"))
                    {
                        var ms = d.GetData("CanIncludeInClipboardHistory") as MemoryStream;
                        if (ms != null && ms.Length >= 4)
                        {
                            var b = new byte[4];
                            ms.Read(b, 0, 4);
                            if (BitConverter.ToInt32(b, 0) == 0) return null;
                        }
                    }
                    if (!d.GetDataPresent(DataFormats.UnicodeText)) return null;
                    return d.GetData(DataFormats.UnicodeText) as string;
                }
                catch (ExternalException) { System.Threading.Thread.Sleep(40); }
                catch (Exception) { return null; }
            }
            return null;
        }

        void SetClipboard(string text, bool ignore)
        {
            if (string.IsNullOrEmpty(text)) return;
            ignoreText = ignore ? text : null;
            try { Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), true, 10, 50); }
            catch (Exception) { ShowStatus("Klembord is bezet door een ander programma, probeer opnieuw."); }
        }

        void AddHistory(string text)
        {
            var existing = data.History.FirstOrDefault(c => c.Text == text);
            if (existing != null) data.History.Remove(existing);
            data.History.Insert(0, new Clip { Text = text, Time = DateTime.Now });
            TrimHistory();
            RefreshHistory();
            ScheduleSave();
        }

        void TrimHistory()
        {
            while (data.History.Count > data.MaxHistory) data.History.RemoveAt(data.History.Count - 1);
        }

        // ------------------------------------------------------------------ Pasting

        static void After(int ms, Action action)
        {
            var t = new Timer { Interval = ms };
            t.Tick += delegate { t.Stop(); t.Dispose(); action(); };
            t.Start();
        }

        void CopyOnly(Clip c)
        {
            if (c == null) return;
            SetClipboard(c.Text, false);
            ShowStatus("Gekopieerd naar het klembord.");
        }

        /// <summary>Paste a clip into the window that was active before this one.</summary>
        void PasteClip(Clip c)
        {
            if (c == null) return;
            SetClipboard(c.Text, false);
            FocusTargetAndPaste(true);
        }

        void FocusTargetAndPaste(bool suppressSequence)
        {
            IntPtr target = lastTarget;
            if (target == IntPtr.Zero || !Native.IsWindow(target))
            {
                ShowStatus("Geen doelvenster gevonden. Klik eerst in het programma waarin je wilt plakken.");
                return;
            }
            if (data.HideAfterPaste) Hide();
            if (Native.IsIconic(target)) Native.ShowWindow(target, 9);
            Native.SetForegroundWindow(target);
            After(90, () => SendPaste(suppressSequence));
        }

        void SendPaste(bool suppressSequence)
        {
            if (suppressSequence) suppressUntil = Environment.TickCount + 600;
            Native.SendCtrlV();
            // A normal paste replaced the clipboard; put the waiting sequence item back afterwards.
            if (suppressSequence && seqActive) After(500, LoadSequenceItem);
        }

        void PasteViaHotkey(Clip c)
        {
            IntPtr fg = Native.GetForegroundWindow();
            uint pid;
            Native.GetWindowThreadProcessId(fg, out pid);
            if (Native.IsKeyDown(Native.VK_MENU) || Native.IsKeyDown(Native.VK_LWIN) || Native.IsKeyDown(Native.VK_RWIN))
                Native.MaskMenuKey();
            SetClipboard(c.Text, false);
            WhenModifiersReleased(() =>
            {
                if (pid == myPid) FocusTargetAndPaste(true);
                else SendPaste(true);
            });
        }

        static void WhenModifiersReleased(Action action)
        {
            int start = Environment.TickCount;
            var t = new Timer { Interval = 25 };
            t.Tick += delegate
            {
                if (Native.AnyModifierDown() && Environment.TickCount - start < 2000) return;
                t.Stop();
                t.Dispose();
                action();
            };
            t.Start();
        }

        // ------------------------------------------------------------------ Hotkeys

        void UnregisterAllHotkeys()
        {
            if (!IsHandleCreated) return;
            Native.UnregisterHotKey(Handle, HK_MAIN);
            foreach (int id in hotkeyMap.Keys) Native.UnregisterHotKey(Handle, id);
            hotkeyMap.Clear();
        }

        void RegisterAllHotkeys()
        {
            if (!IsHandleCreated) return;
            UnregisterAllHotkeys();
            failedHotkeys.Clear();
            mainHotkeyFailed = data.MainKey != 0 &&
                !Native.RegisterHotKey(Handle, HK_MAIN, data.MainMods | Native.MOD_NOREPEAT, data.MainKey);
            for (int i = 0; i < data.Favorites.Count; i++)
            {
                Clip c = data.Favorites[i];
                if (c.Key == 0) continue;
                int id = HK_FAV_BASE + i;
                if (Native.RegisterHotKey(Handle, id, c.Mods | Native.MOD_NOREPEAT, c.Key)) hotkeyMap[id] = c;
                else failedHotkeys.Add(c);
            }
            favList.Invalidate();
        }

        void OnHotkey(int id)
        {
            if (id == HK_MAIN) { ToggleWindow(); return; }
            Clip c;
            if (hotkeyMap.TryGetValue(id, out c)) PasteViaHotkey(c);
        }

        void SetClipHotkey(Clip c)
        {
            if (c == null) return;
            using (var dlg = new HotkeyDialog("Sneltoets voor knipsel", c.Mods, c.Key))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (dlg.Key != 0 && dlg.Key == data.MainKey && dlg.Mods == data.MainMods)
                {
                    MessageBox.Show(this, "Deze combinatie is al de sneltoets voor het venster zelf.", "Klembord Tool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (dlg.Key != 0)
                    foreach (var other in data.Favorites)
                        if (other != c && other.Key == dlg.Key && other.Mods == dlg.Mods) { other.Key = 0; other.Mods = 0; }
                c.Mods = dlg.Mods;
                c.Key = dlg.Key;
            }
            RegisterAllHotkeys();
            RefreshFavorites();
            ScheduleSave();
            if (failedHotkeys.Contains(c))
                MessageBox.Show(this, "Deze sneltoets wordt al door een ander programma gebruikt. Kies een andere combinatie.",
                    "Klembord Tool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else if (c.Key != 0)
                ShowStatus("Sneltoets " + Clip.HotkeyText(c.Mods, c.Key) + " plakt nu dit knipsel.");
        }

        void SetMainHotkey()
        {
            using (var dlg = new HotkeyDialog("Sneltoets voor het venster", data.MainMods, data.MainKey))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                data.MainMods = dlg.Mods;
                data.MainKey = dlg.Key;
            }
            RegisterAllHotkeys();
            ScheduleSave();
            if (mainHotkeyFailed)
                MessageBox.Show(this, "Deze sneltoets wordt al door een ander programma gebruikt. Kies een andere combinatie.",
                    "Klembord Tool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // ------------------------------------------------------------------ List editing

        void AddTo(ClipList target, Clip c)
        {
            if (c == null) return;
            OnDropped(target, new DragPayload { Item = c }, ListOf(target).Count, false);
            ShowStatus(target == favList ? "Toegevoegd aan de bewaarlijst." : "Toegevoegd aan de reekslijst.");
        }

        void OnDropped(ClipList target, object payload, int idx, bool isMove)
        {
            List<Clip> list = ListOf(target);
            Clip current = CurrentSequenceClip();
            var p = payload as DragPayload;
            Clip placed;
            if (isMove && p != null)
            {
                int old = list.IndexOf(p.Item);
                if (old < 0) return;
                list.RemoveAt(old);
                if (old < idx) idx--;
                placed = p.Item;
            }
            else
            {
                string text = p != null ? p.Item.Text : payload as string;
                if (string.IsNullOrEmpty(text)) return;
                if (target == favList)
                {
                    var existing = list.FirstOrDefault(c => c.Text == text);
                    if (existing != null)
                    {
                        favList.SelectedItem = existing;
                        ShowStatus("Dit knipsel staat al in de bewaarlijst.");
                        return;
                    }
                }
                placed = new Clip { Text = text, Time = DateTime.Now };
            }
            list.Insert(Math.Max(0, Math.Min(idx, list.Count)), placed);
            AfterListChanged(target, current);
            target.SelectedItem = placed;
        }

        void RemoveClip(ClipList l, Clip c)
        {
            if (c == null) return;
            Clip current = CurrentSequenceClip();
            int idx = l.SelectedIndex;
            ListOf(l).Remove(c);
            AfterListChanged(l, current);
            if (l.Items.Count > 0) l.SelectedIndex = Math.Min(idx, l.Items.Count - 1);
        }

        void EditClip(ClipList l, Clip c)
        {
            if (c == null) return;
            using (var dlg = new EditDialog("Knipsel bewerken", "Pas de tekst aan:", c.Text))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Value.Length == 0) return;
                c.Text = dlg.Value;
            }
            AfterListChanged(l, CurrentSequenceClip());
        }

        void NewFavorite()
        {
            using (var dlg = new EditDialog("Nieuw knipsel", "Tekst voor het nieuwe knipsel in de bewaarlijst:", ""))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Value.Length == 0) return;
                OnDropped(favList, dlg.Value, data.Favorites.Count, false);
            }
        }

        void AddLinesToSequence()
        {
            string start = "";
            try { if (Clipboard.ContainsText()) start = Clipboard.GetText(); } catch (Exception) { }
            using (var dlg = new EditDialog("Regels toevoegen aan reeks",
                "Elke niet-lege regel wordt een apart item in de reekslijst (ingevuld met de huidige klembord-inhoud):", start))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                Clip current = CurrentSequenceClip();
                int added = 0;
                foreach (string line in dlg.Value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                {
                    if (line.Trim().Length == 0) continue;
                    data.Sequence.Add(new Clip { Text = line, Time = DateTime.Now });
                    added++;
                }
                AfterListChanged(seqList, current);
                ShowStatus(added + " regels toegevoegd aan de reekslijst.");
            }
        }

        void ClearHistory()
        {
            if (data.History.Count == 0) return;
            if (MessageBox.Show(this, "De hele geschiedenis wissen?", "Klembord Tool", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            data.History.Clear();
            RefreshHistory();
            ScheduleSave();
        }

        void ClearSequence()
        {
            if (data.Sequence.Count == 0) return;
            if (MessageBox.Show(this, "De reekslijst leegmaken?", "Klembord Tool", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            StopSequence();
            data.Sequence.Clear();
            RefreshSequence();
            ScheduleSave();
        }

        void AfterListChanged(ClipList l, Clip currentSeqClip)
        {
            if (l == favList)
            {
                RegisterAllHotkeys();
                RefreshFavorites();
            }
            else if (l == seqList)
            {
                if (seqActive)
                {
                    if (data.Sequence.Count == 0) StopSequence();
                    else
                    {
                        int idx = data.Sequence.IndexOf(currentSeqClip);
                        seqIndex = idx >= 0 ? idx : Math.Min(seqIndex, data.Sequence.Count - 1);
                        LoadSequenceItem();
                    }
                }
                RefreshSequence();
            }
            else RefreshHistory();
            ScheduleSave();
        }

        // ------------------------------------------------------------------ Sequence

        Clip CurrentSequenceClip()
        {
            return seqActive && seqIndex >= 0 && seqIndex < data.Sequence.Count ? data.Sequence[seqIndex] : null;
        }

        void StartSequence(int index, bool pasteNow)
        {
            if (data.Sequence.Count == 0)
            {
                ShowStatus("De reekslijst is leeg.");
                return;
            }
            seqIndex = Math.Max(0, Math.Min(index, data.Sequence.Count - 1));
            seqActive = true;
            InstallKeyboardHook();
            LoadSequenceItem();
            if (pasteNow) FocusTargetAndPaste(false);
            else ShowStatus("Reeks staat klaar: druk Ctrl+V in je programma om regel " + (seqIndex + 1) + " te plakken.");
        }

        void LoadSequenceItem()
        {
            Clip c = CurrentSequenceClip();
            if (c == null) return;
            SetClipboard(c.Text, true);
            UpdateSequenceStatus();
            seqList.Invalidate();
            if (seqIndex < seqList.Items.Count) seqList.TopIndex = Math.Max(0, Math.Min(seqList.TopIndex, seqIndex));
        }

        void AdvanceSequence()
        {
            if (!seqActive) return;
            seqIndex++;
            if (seqIndex >= data.Sequence.Count)
            {
                if (data.LoopSequence && data.Sequence.Count > 0) seqIndex = 0;
                else
                {
                    StopSequence();
                    tray.ShowBalloonTip(2000, "Klembord Tool", "Reeks klaar: alle regels zijn geplakt.", ToolTipIcon.Info);
                    return;
                }
            }
            LoadSequenceItem();
        }

        void StopSequence()
        {
            seqActive = false;
            advanceTimer.Stop();
            RemoveKeyboardHook();
            RefreshSequence();
        }

        void InstallKeyboardHook()
        {
            if (kbHook != IntPtr.Zero) return;
            kbProc = KeyboardHook;
            kbHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, kbProc, Native.GetModuleHandle(null), 0);
            ctrlDown = Native.IsKeyDown(Native.VK_CONTROL);
            vPending = false;
        }

        void RemoveKeyboardHook()
        {
            if (kbHook != IntPtr.Zero) Native.UnhookWindowsHookEx(kbHook);
            kbHook = IntPtr.Zero;
        }

        // Watches for Ctrl+V anywhere; once V is released the next sequence item is loaded.
        IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                int msg = wParam.ToInt32();
                bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
                bool up = msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP;
                if (k.vkCode == Native.VK_CONTROL || k.vkCode == Native.VK_LCONTROL || k.vkCode == Native.VK_RCONTROL)
                {
                    if (down) ctrlDown = true;
                    else if (up) ctrlDown = false;
                }
                else if (k.vkCode == Native.VK_V)
                {
                    if (down && ctrlDown) vPending = true;
                    else if (up && vPending)
                    {
                        vPending = false;
                        if (seqActive && Environment.TickCount - suppressUntil >= 0)
                        {
                            advanceTimer.Stop();
                            advanceTimer.Start();
                        }
                    }
                }
            }
            return Native.CallNextHookEx(kbHook, nCode, wParam, lParam);
        }

        // ------------------------------------------------------------------ Startup

        static bool StartupEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(RunName) != null;
        }

        void SetStartup(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\" /tray");
                    else k.DeleteValue(RunName, false);
                }
                ShowStatus(on ? "Klembord Tool start voortaan met Windows (in het systeemvak)." : "Starten met Windows uitgezet.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Kon de instelling niet opslaan: " + ex.Message, "Klembord Tool");
            }
        }
    }
}
