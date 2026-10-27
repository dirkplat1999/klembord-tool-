using System;
using System.Drawing;
using System.Windows.Forms;

namespace KlembordTool
{
    internal class EditDialog : Form
    {
        readonly TextBox box;

        public string Value { get { return box.Text; } }

        public EditDialog(string title, string hint, string text)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9.75F);
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 360);
            MinimumSize = new Size(360, 240);
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var label = new Label { Text = hint, AutoSize = true, ForeColor = Theme.Sub, Margin = new Padding(0, 0, 0, 6) };
            box = new TextBox
            {
                Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Both,
                WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 10.5F),
                Text = (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n")
            };

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            var cancel = new Button { Text = "Annuleren", DialogResult = DialogResult.Cancel, AutoSize = true, FlatStyle = FlatStyle.System };
            var ok = new Button { Text = "OK (Ctrl+Enter)", DialogResult = DialogResult.OK, AutoSize = true, FlatStyle = FlatStyle.System };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            CancelButton = cancel;

            layout.Controls.Add(label, 0, 0);
            layout.Controls.Add(box, 0, 1);
            layout.Controls.Add(buttons, 0, 2);
            Controls.Add(layout);
            ResumeLayout(false);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            box.Focus();
            box.SelectionStart = box.TextLength;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Enter))
            {
                DialogResult = DialogResult.OK;
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    internal class HotkeyDialog : Form
    {
        public uint Mods, Key;
        readonly Label display, hint;
        readonly Button ok;

        public HotkeyDialog(string title, uint mods, uint key)
        {
            Mods = mods;
            Key = key;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9.75F);
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(420, 190);
            BackColor = Theme.Bg;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(14) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.Controls.Add(new Label
            {
                Text = "Druk nu de gewenste toetscombinatie in (bijv. Ctrl+Alt+1) en klik daarna op OK.",
                AutoSize = true, MaximumSize = new Size(390, 0)
            }, 0, 0);
            display = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, BackColor = Theme.Card,
                Font = new Font("Segoe UI Semibold", 15F), ForeColor = Theme.Accent, Margin = new Padding(0, 8, 0, 4),
                Text = key == 0 ? "—" : Clip.HotkeyText(mods, key)
            };
            layout.Controls.Add(display, 0, 1);
            hint = new Label { AutoSize = true, ForeColor = Theme.Sub, Text = " " };
            layout.Controls.Add(hint, 0, 2);

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
            var cancel = new Button { Text = "Annuleren", DialogResult = DialogResult.Cancel, AutoSize = true, FlatStyle = FlatStyle.System, TabStop = false };
            ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, FlatStyle = FlatStyle.System, TabStop = false };
            var clear = new Button { Text = "Geen sneltoets", AutoSize = true, FlatStyle = FlatStyle.System, TabStop = false };
            clear.Click += delegate { Mods = 0; Key = 0; DialogResult = DialogResult.OK; };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            buttons.Controls.Add(clear);
            layout.Controls.Add(buttons, 0, 3);
            Controls.Add(layout);
            ResumeLayout(false);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            uint m = 0;
            if ((keyData & Keys.Control) != 0) m |= Native.MOD_CONTROL;
            if ((keyData & Keys.Alt) != 0) m |= Native.MOD_ALT;
            if ((keyData & Keys.Shift) != 0) m |= Native.MOD_SHIFT;
            if (Native.IsKeyDown(Native.VK_LWIN) || Native.IsKeyDown(Native.VK_RWIN)) m |= Native.MOD_WIN;

            if (k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin || k == Keys.None)
            {
                string partial = Clip.HotkeyText(m, (uint)Keys.F1);
                display.Text = m == 0 ? "—" : partial.Substring(0, partial.Length - 2) + "…";
                return true;
            }

            bool fkey = k >= Keys.F1 && k <= Keys.F24;
            if (!fkey && (m == 0 || m == Native.MOD_SHIFT))
            {
                hint.Text = "Gebruik minstens Ctrl, Alt of Win (of een F-toets).";
                return true;
            }
            Mods = m;
            Key = (uint)k;
            display.Text = Clip.HotkeyText(m, Key);
            hint.Text = " ";
            return true;
        }
    }
}
