using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Lichtbak
{
    /// <summary>
    /// App Exposé for Lichtbak: every Lichtbak window as a tile on a dark full-screen layer,
    /// without the row of desktops that Task View always shows. Click a tile (or use the arrow
    /// keys and Enter) to bring that window forward; Esc or a click elsewhere closes it.
    /// Tiles show each window's current picture, so minimised windows appear too.
    /// </summary>
    internal class Overview : Form
    {
        private readonly List<ViewerForm> _windows;
        private readonly List<Rectangle> _tiles = new List<Rectangle>();
        private int _hover = -1, _selected;
        private readonly float _dpi;
        private bool _closing;

        public Overview(List<ViewerForm> windows, ViewerForm current)
        {
            _windows = windows;
            _selected = Math.Max(0, windows.IndexOf(current));
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.FromPoint(Cursor.Position).Bounds;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            BackColor = Color.FromArgb(18, 18, 18);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            using (Graphics g = CreateGraphics()) _dpi = g.DpiX / 96f;
            Layout2();
        }

        private int D(float v) { return (int)Math.Round(v * _dpi); }

        /// <summary>Picks the column count that gives the biggest tiles.</summary>
        private void Layout2()
        {
            _tiles.Clear();
            int n = _windows.Count;
            if (n == 0) return;
            int margin = D(56), gap = D(28), label = D(30);
            int w = ClientSize.Width - margin * 2, h = ClientSize.Height - margin * 2;
            int bestCols = 1; float bestSize = 0;
            for (int cols = 1; cols <= n; cols++)
            {
                int rows = (n + cols - 1) / cols;
                float cw = (w - gap * (cols - 1)) / (float)cols, ch = (h - gap * (rows - 1)) / (float)rows - label;
                float size = Math.Min(cw / 1.5f, ch);
                if (size > bestSize) { bestSize = size; bestCols = cols; }
            }
            int r = (n + bestCols - 1) / bestCols;
            int cellW = (w - gap * (bestCols - 1)) / bestCols, cellH = (h - gap * (r - 1)) / r;
            cellW = Math.Min(cellW, D(720));
            cellH = Math.Min(cellH, D(520));
            int totalH = r * cellH + (r - 1) * gap;
            for (int i = 0; i < n; i++)
            {
                int row = i / bestCols, col = i % bestCols;
                int inRow = Math.Min(bestCols, n - row * bestCols);
                int rowW = inRow * cellW + (inRow - 1) * gap;
                int x = (ClientSize.Width - rowW) / 2 + col * (cellW + gap);
                int y = (ClientSize.Height - totalH) / 2 + row * (cellH + gap);
                _tiles.Add(new Rectangle(x, y, cellW, cellH));
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!_closing) Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _closing = true;
            base.OnFormClosing(e);
        }

        /// <summary>For the self-test: lays out again after the bounds were changed.</summary>
        internal void Relayout() { Layout2(); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            int label = D(30);
            using (Font font = Modern.Font(10f, FontStyle.Regular))
            {
                for (int i = 0; i < _tiles.Count; i++)
                {
                    Rectangle cell = _tiles[i];
                    var area = new Rectangle(cell.X, cell.Y, cell.Width, cell.Height - label);
                    Bitmap picture = _windows[i].CurrentBitmap;
                    Rectangle shown = area;
                    if (picture != null)
                    {
                        float s = Math.Min((float)area.Width / picture.Width, (float)area.Height / picture.Height);
                        int pw = (int)(picture.Width * s), ph = (int)(picture.Height * s);
                        shown = new Rectangle(area.X + (area.Width - pw) / 2, area.Y + (area.Height - ph) / 2, pw, ph);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(picture, shown);
                    }
                    else
                    {
                        shown = Rectangle.Inflate(area, -area.Width / 4, -area.Height / 4);
                        using (var b = new SolidBrush(Color.FromArgb(50, 50, 50))) g.FillRectangle(b, shown);
                    }

                    bool active = i == _hover || (_hover < 0 && i == _selected);
                    if (active)
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        using (var pen = new Pen(Modern.Mix(Modern.Accent, Color.White, 0.35f), D(3)))
                        using (GraphicsPath path = Modern.Round(RectangleF.Inflate(shown, D(5), D(5)), D(6)))
                            g.DrawPath(pen, path);
                        g.SmoothingMode = SmoothingMode.None;
                    }
                    var text = new Rectangle(cell.X, shown.Bottom + D(10), cell.Width, label - D(6));
                    TextRenderer.DrawText(g, _windows[i].CurrentName, font, text,
                        active ? Color.White : Color.FromArgb(180, 180, 180),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.SingleLine |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
            }
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < _tiles.Count; i++) if (_tiles[i].Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = HitTest(e.Location);
            if (i != _hover) { _hover = i; Cursor = i >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = HitTest(e.Location);
            if (i >= 0 && e.Button == MouseButtons.Left) Pick(i);
            else Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            int cols = 1;
            for (int i = 1; i < _tiles.Count; i++) if (_tiles[i].Y == _tiles[0].Y) cols++;
            int sel = _hover >= 0 ? _hover : _selected;
            switch (keyData)
            {
                case Keys.Escape: case Keys.F3: Close(); return true;
                case Keys.Enter: case Keys.Space: Pick(sel); return true;
                case Keys.Right: case Keys.Tab: sel++; break;
                case Keys.Left: case Keys.Shift | Keys.Tab: sel--; break;
                case Keys.Down: sel += cols; break;
                case Keys.Up: sel -= cols; break;
                default: return base.ProcessCmdKey(ref msg, keyData);
            }
            _hover = -1;
            _selected = Math.Max(0, Math.Min(_windows.Count - 1, sel));
            Invalidate();
            return true;
        }

        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);

        private void Pick(int i)
        {
            if (i < 0 || i >= _windows.Count) return;
            ViewerForm w = _windows[i];
            Close();
            if (w.IsDisposed) return;
            if (w.WindowState == FormWindowState.Minimized) w.WindowState = FormWindowState.Normal;
            SetForegroundWindow(w.Handle);
            w.Activate();
        }
    }
}
