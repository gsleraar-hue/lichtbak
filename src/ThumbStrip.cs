using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Lichtbak
{
    /// <summary>
    /// The sidebar: a scrolling column of thumbnails with the file name under each, as in
    /// Preview. Click to show, drag to reorder, drag out to copy the file elsewhere.
    /// </summary>
    internal class ThumbStrip : ScrollableControl
    {
        private const string InternalFormat = "Lichtbak.Index";

        public List<Picture> Items = new List<Picture>();
        public int Selected = -1;

        public event Action<int> Picked;
        public event Action<int, Point> MenuRequested;
        public event Action<int, int> Moved;        // from, to (index after removal)
        public event Action<string[]> FilesDropped;

        private float _dpi = 1f;
        private int _pad, _cellH, _boxW, _boxH, _nameH;
        private int _pressed = -1;
        private Point _pressAt;
        private int _insertAt = -1;

        public ThumbStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AutoScroll = true;
            AllowDrop = true;
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            using (Graphics g = CreateGraphics()) _dpi = g.DpiX / 96f;
            ApplyTheme();
            Relayout();
        }

        public void ApplyTheme()
        {
            if (IsHandleCreated) SetWindowTheme(Handle, Modern.Dark ? "DarkMode_Explorer" : "Explorer", null);
            Invalidate();
        }

        private int D(float v) { return (int)Math.Round(v * _dpi); }

        public void Relayout()
        {
            _pad = D(10);
            _nameH = D(22);
            int scrollbar = SystemInformation.VerticalScrollBarWidth;
            _boxW = Math.Max(D(40), ClientSize.Width - _pad * 2 - (VerticalScroll.Visible ? 0 : scrollbar));
            _boxH = (int)(_boxW * 0.78f);
            _cellH = _boxH + _nameH + _pad;
            AutoScrollMinSize = new Size(0, Items.Count * _cellH + _pad);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (IsHandleCreated) Relayout();
        }

        private int ScrollY { get { return -AutoScrollPosition.Y; } }

        private Rectangle Cell(int i)
        {
            return new Rectangle(_pad, _pad + i * _cellH - ScrollY, _boxW, _boxH + _nameH);
        }

        private int HitTest(Point p)
        {
            if (_cellH <= 0) return -1;
            int i = (p.Y + ScrollY - _pad) / _cellH;
            if (p.Y + ScrollY - _pad < 0 || i < 0 || i >= Items.Count) return -1;
            return i;
        }

        public int FirstVisible { get { return _cellH <= 0 ? 0 : Math.Max(0, ScrollY / _cellH); } }
        public int LastVisible { get { return _cellH <= 0 ? 0 : Math.Min(Items.Count - 1, (ScrollY + ClientSize.Height) / _cellH); } }

        public void EnsureVisible(int i)
        {
            if (i < 0 || i >= Items.Count || _cellH <= 0) return;
            int top = _pad + i * _cellH, bottom = top + _cellH;
            int y = ScrollY;
            if (top - _pad < y) y = top - _pad;
            else if (bottom > y + ClientSize.Height) y = bottom - ClientSize.Height;
            AutoScrollPosition = new Point(0, Math.Max(0, y));
            Invalidate();
        }

        // ------------------------------------------------------------- painting

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            if (Items.Count == 0 || _cellH <= 0) return;

            using (Font font = Modern.Font(8.5f, FontStyle.Regular))
            {
                for (int i = FirstVisible; i <= LastVisible && i < Items.Count; i++)
                    DrawItem(g, i, font);
            }

            if (_insertAt >= 0)
            {
                int y = _pad / 2 + _insertAt * _cellH - ScrollY;
                using (var pen = new Pen(Modern.AccentFill, D(2.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.DrawLine(pen, _pad, y, _pad + _boxW, y);
                }
            }
        }

        private void DrawItem(Graphics g, int i, Font font)
        {
            Picture p = Items[i];
            Rectangle cell = Cell(i);
            var box = new Rectangle(cell.X, cell.Y, _boxW, _boxH);
            bool selected = i == Selected;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (selected)
            {
                using (GraphicsPath path = Modern.Round(RectangleF.Inflate(box, D(4), D(4)), D(6)))
                using (var fill = new SolidBrush(Modern.Dark ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0)))
                    g.FillPath(fill, path);
            }

            Bitmap thumb = p.Thumb;
            if (thumb != null)
            {
                float scale = Math.Min((float)box.Width / thumb.Width, (float)box.Height / thumb.Height);
                scale = Math.Min(scale, 1f * _dpi * 2);
                int w = Math.Max(1, (int)(thumb.Width * scale)), h = Math.Max(1, (int)(thumb.Height * scale));
                var r = new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
                using (var shadow = new SolidBrush(Color.FromArgb(Modern.Dark ? 90 : 40, 0, 0, 0)))
                    g.FillRectangle(shadow, r.X + 1, r.Y + 1, r.Width + 1, r.Height + 1);
                g.SmoothingMode = SmoothingMode.None;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(thumb, r);
            }
            else
            {
                var r = Rectangle.Inflate(box, -D(14), -D(14));
                using (GraphicsPath path = Modern.Round(r, D(4)))
                using (var fill = new SolidBrush(Modern.Stroke))
                    g.FillPath(fill, path);
                if (p.Error != null)
                    TextRenderer.DrawText(g, "?", font, r, Modern.SubText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            // Name under the picture; the selected one gets an accent pill.
            var nameRect = new Rectangle(cell.X, box.Bottom + D(5), _boxW, _nameH - D(4));
            Size size = TextRenderer.MeasureText(g, p.Name, font, nameRect.Size, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            int pillW = Math.Min(nameRect.Width, size.Width + D(12));
            var pill = new Rectangle(nameRect.X + (nameRect.Width - pillW) / 2, nameRect.Y, pillW, nameRect.Height);
            Color textColor = Modern.SubText;
            if (selected)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = Modern.Round(pill, D(4)))
                using (var fill = new SolidBrush(Modern.AccentFill))
                    g.FillPath(fill, path);
                textColor = Modern.OnAccent;
            }
            TextRenderer.DrawText(g, p.Name, font, Rectangle.Inflate(pill, -D(4), 0), textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate();
        }

        // ------------------------------------------------------------- mouse

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int i = HitTest(e.Location);
            if (i < 0) return;
            if (e.Button == MouseButtons.Left)
            {
                _pressed = i;
                _pressAt = e.Location;
                if (Picked != null) Picked(i);
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (Picked != null) Picked(i);
                if (MenuRequested != null) MenuRequested(i, e.Location);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_pressed < 0 || e.Button != MouseButtons.Left) return;
            Size drag = SystemInformation.DragSize;
            if (Math.Abs(e.X - _pressAt.X) < drag.Width && Math.Abs(e.Y - _pressAt.Y) < drag.Height) return;

            int index = _pressed;
            _pressed = -1;
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, new[] { Items[index].Path });
            data.SetData(InternalFormat, index);
            DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move);
            _insertAt = -1;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _pressed = -1;
        }

        // ------------------------------------------------------------- drag and drop

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            if (e.Data.GetDataPresent(InternalFormat))
            {
                e.Effect = DragDropEffects.Move;
                Point p = PointToClient(new Point(e.X, e.Y));
                int at = (int)Math.Round((p.Y + ScrollY - _pad) / (float)_cellH);
                at = Math.Max(0, Math.Min(Items.Count, at));
                AutoScrollNearEdge(p);
                if (at != _insertAt) { _insertAt = at; Invalidate(); }
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            else e.Effect = DragDropEffects.None;
        }

        private void AutoScrollNearEdge(Point p)
        {
            int edge = D(30), step = D(20);
            if (p.Y < edge) AutoScrollPosition = new Point(0, Math.Max(0, ScrollY - step));
            else if (p.Y > ClientSize.Height - edge) AutoScrollPosition = new Point(0, ScrollY + step);
            else return;
            Invalidate();
        }

        protected override void OnDragLeave(EventArgs e)
        {
            base.OnDragLeave(e);
            _insertAt = -1;
            Invalidate();
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            if (e.Data.GetDataPresent(InternalFormat))
            {
                int from = (int)e.Data.GetData(InternalFormat);
                int to = _insertAt < 0 ? from : (_insertAt > from ? _insertAt - 1 : _insertAt);
                _insertAt = -1;
                Invalidate();
                if (to != from && Moved != null) Moved(from, to);
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && FilesDropped != null) FilesDropped(files);
            }
        }
    }
}
