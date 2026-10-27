using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KlembordTool
{
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(243, 243, 243);
        public static readonly Color Card = Color.White;
        public static readonly Color Text = Color.FromArgb(27, 27, 27);
        public static readonly Color Sub = Color.FromArgb(112, 112, 112);
        public static readonly Color Accent = Color.FromArgb(0, 103, 192);
        public static readonly Color SelBg = Color.FromArgb(204, 228, 247);
        public static readonly Color HoverBg = Color.FromArgb(240, 246, 252);
        public static readonly Color Line = Color.FromArgb(235, 235, 235);
        public static readonly Color Warn = Color.FromArgb(196, 43, 28);

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Icon MakeIcon()
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var b = new SolidBrush(Accent))
                using (var p = RoundRect(new Rectangle(4, 4, 24, 27), 4))
                    g.FillPath(b, p);
                using (var w = new SolidBrush(Color.White))
                    g.FillRectangle(w, 8, 9, 16, 18);
                using (var c = new SolidBrush(Color.FromArgb(45, 45, 45)))
                using (var p = RoundRect(new Rectangle(10, 1, 12, 7), 2))
                    g.FillPath(c, p);
                using (var pen = new Pen(Color.FromArgb(140, 140, 140), 2f))
                {
                    g.DrawLine(pen, 11, 14, 21, 14);
                    g.DrawLine(pen, 11, 18, 21, 18);
                    g.DrawLine(pen, 11, 22, 18, 22);
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }
    }

    internal class DragPayload
    {
        public ClipList Source;
        public Clip Item;
    }

    /// <summary>Owner-drawn list of clips with click-to-paste and drag &amp; drop.</summary>
    internal class ClipList : ListBox
    {
        public const string DragFormat = "KlembordToolClip";

        public Func<Clip, int, string> Meta;
        public Func<int, bool> IsMarked;
        public Func<Clip, bool> IsWarning;
        public bool AcceptDrops, AllowReorder;
        public string EmptyText = "";

        public event Action<Clip> ItemClicked;
        public event Action<object, int, bool> ItemDropped;

        Point downPt;
        int downIdx = -1, hoverIdx = -1, dropIdx = -1;
        bool dragging;
        Font smallFont, boldFont;
        readonly ToolTip tip = new ToolTip { InitialDelay = 800, AutoPopDelay = 15000 };

        public ClipList()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            IntegralHeight = false;
            BorderStyle = BorderStyle.None;
            AllowDrop = true;
            DoubleBuffered = true;
            UpdateMetrics();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            UpdateMetrics();
        }

        void UpdateMetrics()
        {
            if (smallFont != null) smallFont.Dispose();
            if (boldFont != null) boldFont.Dispose();
            smallFont = new Font(Font.FontFamily, Font.Size * 0.85f);
            boldFont = new Font(Font, FontStyle.Bold);
            ItemHeight = Math.Min(255, Font.Height + smallFont.Height + Font.Height / 2 + 4);
        }

        static string Preview(string text)
        {
            if (text == null) return "";
            string s = text.Length > 400 ? text.Substring(0, 400) : text;
            s = s.Replace("\r\n", " ⏎ ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
            return s.Trim();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            var c = (Clip)Items[e.Index];
            var g = e.Graphics;
            var r = e.Bounds;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            bool marked = IsMarked != null && IsMarked(e.Index);

            Color bg = sel ? Theme.SelBg : (e.Index == hoverIdx ? Theme.HoverBg : BackColor);
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, r);
            if (marked)
                using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, r.X, r.Y + 3, 4, r.Height - 6);

            int pad = Font.Height / 2;
            int x = r.X + pad + (marked ? 4 : 0);
            int w = r.Width - (x - r.X) - pad;
            int top = r.Y + (r.Height - Font.Height - smallFont.Height) / 2;
            var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

            TextRenderer.DrawText(g, Preview(c.Text), marked ? boldFont : Font,
                new Rectangle(x, top, w, Font.Height), Theme.Text, flags);
            string meta = Meta != null ? Meta(c, e.Index) : "";
            Color metaColor = IsWarning != null && IsWarning(c) ? Theme.Warn : (marked ? Theme.Accent : Theme.Sub);
            TextRenderer.DrawText(g, meta, smallFont,
                new Rectangle(x, top + Font.Height, w, smallFont.Height), metaColor, flags);

            using (var p = new Pen(Theme.Line)) g.DrawLine(p, r.X + pad, r.Bottom - 1, r.Right - pad, r.Bottom - 1);

            if (dropIdx == e.Index || (dropIdx == Items.Count && e.Index == Items.Count - 1))
            {
                int y = dropIdx == e.Index ? r.Y : r.Bottom - 3;
                using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, r.X, y, r.Width, 3);
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // Draw a hint in empty lists (WM_PAINT).
            if (m.Msg == 0x000F && Items.Count == 0 && EmptyText.Length > 0)
            {
                using (var g = CreateGraphics())
                {
                    var rect = ClientRectangle;
                    rect.Inflate(-Font.Height, -Font.Height);
                    TextRenderer.DrawText(g, EmptyText, Font, rect, Theme.Sub,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }

        void SetHover(int idx)
        {
            if (idx == hoverIdx) return;
            int old = hoverIdx;
            hoverIdx = idx;
            if (old >= 0 && old < Items.Count) Invalidate(GetItemRectangle(old));
            if (idx >= 0 && idx < Items.Count)
            {
                Invalidate(GetItemRectangle(idx));
                string t = ((Clip)Items[idx]).Text ?? "";
                tip.SetToolTip(this, t.Length > 700 ? t.Substring(0, 700) + "…" : t);
            }
            else tip.SetToolTip(this, "");
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            downIdx = IndexFromPoint(e.Location);
            downPt = e.Location;
            dragging = false;
            if (e.Button == MouseButtons.Right && downIdx >= 0) SelectedIndex = downIdx;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = IndexFromPoint(e.Location);
            if (idx >= 0 && !GetItemRectangle(idx).Contains(e.Location)) idx = -1;
            SetHover(idx);

            if (e.Button == MouseButtons.Left && downIdx >= 0 && downIdx < Items.Count && !dragging)
            {
                Size ds = SystemInformation.DragSize;
                if (Math.Abs(e.X - downPt.X) > ds.Width || Math.Abs(e.Y - downPt.Y) > ds.Height)
                {
                    dragging = true;
                    tip.Hide(this);
                    var payload = new DragPayload { Source = this, Item = (Clip)Items[downIdx] };
                    var dobj = new DataObject();
                    dobj.SetData(DragFormat, payload);
                    if (!string.IsNullOrEmpty(payload.Item.Text)) dobj.SetText(payload.Item.Text);
                    DoDragDrop(dobj, DragDropEffects.Copy | DragDropEffects.Move);
                    downIdx = -1;
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && !dragging && downIdx >= 0 && downIdx < Items.Count
                && IndexFromPoint(e.Location) == downIdx && GetItemRectangle(downIdx).Contains(e.Location))
            {
                var c = (Clip)Items[downIdx];
                if (ItemClicked != null) ItemClicked(c);
            }
            downIdx = -1;
            dragging = false;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SetHover(-1);
        }

        object GetDropData(IDataObject d, out bool isMove)
        {
            isMove = false;
            var p = d.GetData(DragFormat) as DragPayload;
            if (p != null)
            {
                if (p.Source == this) { isMove = true; return AllowReorder ? p : null; }
                return AcceptDrops ? p : null;
            }
            if (AcceptDrops && d.GetDataPresent(DataFormats.UnicodeText)) return d.GetData(DataFormats.UnicodeText) as string;
            return null;
        }

        int DropIndexAt(int screenX, int screenY)
        {
            Point pt = PointToClient(new Point(screenX, screenY));
            int idx = IndexFromPoint(pt);
            if (idx < 0 || !GetItemRectangle(idx).Contains(pt)) return Items.Count;
            Rectangle r = GetItemRectangle(idx);
            return pt.Y > r.Y + r.Height / 2 ? idx + 1 : idx;
        }

        void SetDropIdx(int idx)
        {
            if (idx == dropIdx) return;
            dropIdx = idx;
            Invalidate();
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            bool isMove;
            object data = GetDropData(e.Data, out isMove);
            if (data == null) { e.Effect = DragDropEffects.None; SetDropIdx(-1); return; }
            e.Effect = isMove ? DragDropEffects.Move : DragDropEffects.Copy;
            SetDropIdx(DropIndexAt(e.X, e.Y));
        }

        protected override void OnDragLeave(EventArgs e)
        {
            base.OnDragLeave(e);
            SetDropIdx(-1);
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            bool isMove;
            object data = GetDropData(e.Data, out isMove);
            int idx = DropIndexAt(e.X, e.Y);
            SetDropIdx(-1);
            if (data != null && ItemDropped != null) ItemDropped(data, idx, isMove);
        }
    }
}
