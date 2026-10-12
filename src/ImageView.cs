using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Lichtbak
{
    /// <summary>
    /// The big picture: fits the window by default; Ctrl+wheel or pinch zooms at the pointer,
    /// dragging and the wheel pan, double-click toggles between fit and actual size.
    /// </summary>
    internal class ImageView : Control
    {
        private Picture _picture;
        private bool _fit = true;
        private float _zoom = 1f;
        private PointF _center;           // picture coordinates at the middle of the view
        private bool _dragging;
        private Point _dragFrom;
        private PointF _dragCenter;
        private bool _rough;              // cheap interpolation while the user is moving things
        private readonly Timer _settle = new Timer { Interval = 160 };
        private readonly Timer _fade = new Timer { Interval = 30 };
        private string _overlay;
        private DateTime _overlayUntil;
        private float _overlayAlpha;

        public string EmptyText = "";
        /// <summary>Raised when the zoom asks for more pixels than the preview has.</summary>
        public event Action NeedFull;
        public event Action ZoomChanged;
        /// <summary>Wheel without Ctrl while the picture fits: -1 previous, +1 next.</summary>
        public event Action<int> Step;
        /// <summary>The sidebar button in the top-left corner was clicked.</summary>
        public event Action SidebarToggle;
        public bool SidebarShown = true;
        private bool _buttonHover;

        public ImageView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            _settle.Tick += delegate { _settle.Stop(); _rough = false; Invalidate(); };
            _fade.Tick += delegate { FadeStep(); };
        }

        public Picture Picture { get { return _picture; } }

        public void Show(Picture picture)
        {
            _picture = picture;
            _fit = true;
            _dragging = false;
            Cursor = Cursors.Default;
            Invalidate();
        }

        // ------------------------------------------------------------- geometry

        private SizeF ImageSize
        {
            get
            {
                if (_picture == null) return SizeF.Empty;
                if (!_picture.FullSize.IsEmpty) return _picture.FullSize;
                Bitmap b = Source(1f);
                return b == null ? SizeF.Empty : new SizeF(b.Width, b.Height);
            }
        }

        public float FitScale
        {
            get
            {
                SizeF img = ImageSize;
                if (img.Width <= 0 || img.Height <= 0 || Width <= 0 || Height <= 0) return 1f;
                return Math.Min(1f, Math.Min(Width / img.Width, Height / img.Height));
            }
        }

        public float ViewScale { get { return _fit ? FitScale : _zoom; } }
        public bool IsFit { get { return _fit; } }

        public bool WantsFull
        {
            get
            {
                if (_picture == null || _picture.Full != null || _picture.Preview == null) return false;
                return ViewScale * ImageSize.Width > _picture.Preview.Width + 1;
            }
        }

        private Bitmap Source(float scale)
        {
            Picture p = _picture;
            if (p == null) return null;
            SizeF img = p.FullSize.IsEmpty ? SizeF.Empty : (SizeF)p.FullSize;
            if (p.Full != null && (p.Preview == null || img.IsEmpty || scale * img.Width > p.Preview.Width + 1)) return p.Full;
            if (p.Preview != null) return p.Preview;
            return p.Thumb;
        }

        private void Clamp()
        {
            SizeF img = ImageSize;
            float s = ViewScale;
            if (_fit) { _center = new PointF(img.Width / 2, img.Height / 2); return; }
            float halfW = Width / 2f / s, halfH = Height / 2f / s;
            _center.X = img.Width <= halfW * 2 ? img.Width / 2 : Math.Max(halfW, Math.Min(img.Width - halfW, _center.X));
            _center.Y = img.Height <= halfH * 2 ? img.Height / 2 : Math.Max(halfH, Math.Min(img.Height - halfH, _center.Y));
        }

        private bool Pannable
        {
            get
            {
                SizeF img = ImageSize;
                float s = ViewScale;
                return img.Width * s > Width + 0.5f || img.Height * s > Height + 0.5f;
            }
        }

        // ------------------------------------------------------------- zooming

        public void Fit()
        {
            _fit = true;
            Changed();
        }

        public void ActualSize(Point? anchor)
        {
            SetZoom(1f, anchor);
        }

        public void ZoomBy(float factor, Point? anchor)
        {
            SetZoom(ViewScale * factor, anchor);
        }

        private void SetZoom(float zoom, Point? anchor)
        {
            if (_picture == null || ImageSize.IsEmpty) return;
            float fit = FitScale;
            zoom = Math.Max(Math.Min(fit, 0.05f), Math.Min(32f, zoom));
            Point a = anchor ?? new Point(Width / 2, Height / 2);
            float old = ViewScale;
            if (_fit) _center = new PointF(ImageSize.Width / 2, ImageSize.Height / 2);
            // Keep the picture point under the anchor where it is.
            var p = new PointF(_center.X + (a.X - Width / 2f) / old, _center.Y + (a.Y - Height / 2f) / old);
            if (zoom <= fit + 0.0001f) { _fit = true; }
            else
            {
                _fit = false;
                _zoom = zoom;
                _center = new PointF(p.X - (a.X - Width / 2f) / zoom, p.Y - (a.Y - Height / 2f) / zoom);
            }
            Rough();
            Changed();
        }

        private void Changed()
        {
            Clamp();
            Cursor = Pannable ? Cursors.SizeAll : Cursors.Default;
            Invalidate();
            if (WantsFull && NeedFull != null) NeedFull();
            if (ZoomChanged != null) ZoomChanged();
        }

        private void Rough()
        {
            _rough = true;
            _settle.Stop();
            _settle.Start();
        }

        // ------------------------------------------------------------- overlay

        public void Flash(string text)
        {
            _overlay = text;
            _overlayAlpha = 1f;
            _overlayUntil = DateTime.Now.AddMilliseconds(1400);
            _fade.Start();
            Invalidate();
        }

        private void FadeStep()
        {
            if (DateTime.Now < _overlayUntil) return;
            _overlayAlpha -= 0.08f;
            if (_overlayAlpha <= 0) { _overlayAlpha = 0; _fade.Stop(); }
            Invalidate();
        }

        // ------------------------------------------------------------- painting

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Rough();
            if (_picture != null) Changed();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            PaintPicture(e.Graphics);
            DrawSidebarButton(e.Graphics);
        }

        private Rectangle SidebarButton
        {
            get
            {
                float d = DeviceDpi / 96f;
                return new Rectangle((int)(10 * d), (int)(10 * d), (int)(34 * d), (int)(28 * d));
            }
        }

        /// <summary>Preview-style toggle: a window outline with the left panel filled while the sidebar shows.</summary>
        private void DrawSidebarButton(Graphics g)
        {
            Rectangle b = SidebarButton;
            float d = DeviceDpi / 96f;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool onPicture = BackColor.GetBrightness() < 0.5f;
            Color fill = Color.FromArgb(_buttonHover ? 200 : 130, onPicture ? Color.FromArgb(45, 45, 45) : Color.FromArgb(250, 250, 250));
            using (GraphicsPath path = Modern.Round(b, 6 * d))
            using (var brush = new SolidBrush(fill))
                g.FillPath(brush, path);

            Color ink = Color.FromArgb(_buttonHover ? 255 : 210, onPicture ? Color.White : Color.FromArgb(40, 40, 40));
            var icon = new RectangleF(b.X + b.Width / 2f - 8 * d, b.Y + b.Height / 2f - 6.5f * d, 16 * d, 13 * d);
            using (var pen = new Pen(ink, 1.4f * d))
            using (GraphicsPath frame = Modern.Round(icon, 2.5f * d))
            {
                g.DrawPath(pen, frame);
                g.DrawLine(pen, icon.X + 5.5f * d, icon.Y, icon.X + 5.5f * d, icon.Bottom);
            }
            if (SidebarShown)
                using (var brush = new SolidBrush(ink))
                    g.FillRectangle(brush, icon.X + 1.5f * d, icon.Y + 1.5f * d, 4f * d, icon.Height - 3f * d);
        }

        private void PaintPicture(Graphics g)
        {
            g.Clear(BackColor);
            Picture p = _picture;
            if (p == null)
            {
                DrawCentered(g, EmptyText, Modern.SubText, 11f);
                return;
            }

            Clamp();
            SizeF img = ImageSize;
            float s = ViewScale;
            Bitmap src = Source(s);
            if (src == null || img.IsEmpty)
            {
                if (p.Error != null) DrawCentered(g, Strings.T("Cannot open ", "Kan ") + p.Name + Strings.T(".", " niet openen.") + "\n" + p.Error, Modern.SubText, 10f);
                return;
            }

            var dest = new RectangleF(Width / 2f - _center.X * s, Height / 2f - _center.Y * s, img.Width * s, img.Height * s);
            RectangleF visible = RectangleF.Intersect(dest, new RectangleF(0, 0, Width, Height));
            if (visible.Width < 1 || visible.Height < 1) return;
            Rectangle target = Rectangle.Round(visible);
            float k = src.Width / img.Width;  // bitmap pixels per picture pixel
            float sx = (target.X - dest.X) / s * k, sy = (target.Y - dest.Y) / s * k;
            float sw = target.Width / s * k, sh = target.Height / s * k;

            float density = s / k;  // screen pixels per bitmap pixel
            if (density >= 3f) g.InterpolationMode = InterpolationMode.NearestNeighbor;
            else if (_rough) g.InterpolationMode = InterpolationMode.Bilinear;
            else g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = density >= 3f ? PixelOffsetMode.Half : PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighSpeed;

            using (var attributes = new ImageAttributes())
            {
                attributes.SetWrapMode(WrapMode.TileFlipXY);  // no grey seam along the edges
                g.DrawImage(src, target, sx, sy, sw, sh, GraphicsUnit.Pixel, attributes);
            }

            if (_overlayAlpha > 0 && !string.IsNullOrEmpty(_overlay)) DrawOverlay(g);
        }

        private void DrawOverlay(Graphics g)
        {
            using (Font font = Modern.Font(9.5f, FontStyle.Regular))
            {
                Size text = TextRenderer.MeasureText(g, _overlay, font);
                int padX = text.Height, padY = text.Height / 3;
                var r = new RectangleF((Width - text.Width) / 2f - padX, Height - text.Height - padY * 2 - text.Height,
                                       text.Width + padX * 2, text.Height + padY * 2);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = Modern.Round(r, r.Height / 2))
                using (var fill = new SolidBrush(Color.FromArgb((int)(170 * _overlayAlpha), 20, 20, 20)))
                    g.FillPath(fill, path);
                // TextRenderer cannot do alpha, so fade by blending towards the pill colour.
                Color c = Modern.Mix(Color.FromArgb(40, 40, 40), Color.White, _overlayAlpha);
                TextRenderer.DrawText(g, _overlay, font, Rectangle.Round(r), c,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }

        private void DrawCentered(Graphics g, string text, Color color, float size)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (Font font = Modern.Font(size, FontStyle.Regular))
                TextRenderer.DrawText(g, text, font, ClientRectangle, color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }

        // ------------------------------------------------------------- mouse

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && SidebarButton.Contains(e.Location))
            {
                if (SidebarToggle != null) SidebarToggle();
                return;
            }
            Focus();
            if (e.Button == MouseButtons.Left && Pannable)
            {
                _dragging = true;
                _dragFrom = e.Location;
                _dragCenter = _center;
                Capture = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hover = !_dragging && SidebarButton.Contains(e.Location);
            if (hover != _buttonHover)
            {
                _buttonHover = hover;
                Cursor = hover ? Cursors.Hand : (Pannable ? Cursors.SizeAll : Cursors.Default);
                Invalidate(SidebarButton);
            }
            if (!_dragging) return;
            float s = ViewScale;
            _center = new PointF(_dragCenter.X - (e.X - _dragFrom.X) / s, _dragCenter.Y - (e.Y - _dragFrom.Y) / s);
            Rough();
            Clamp();
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_buttonHover) { _buttonHover = false; Invalidate(SidebarButton); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Capture = false;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left || SidebarButton.Contains(e.Location)) return;
            if (_fit && FitScale < 0.999f) ActualSize(e.Location);
            else if (!_fit) Fit();
        }

        private int _wheelRest;

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if ((ModifierKeys & Keys.Control) != 0)
            {
                // Touchpad pinch arrives as Ctrl+wheel with small deltas.
                ZoomBy((float)Math.Pow(1.25, e.Delta / 120.0), e.Location);
                return;
            }
            if (Pannable)
            {
                Pan((ModifierKeys & Keys.Shift) != 0 ? -e.Delta : 0, (ModifierKeys & Keys.Shift) != 0 ? 0 : -e.Delta);
                return;
            }
            _wheelRest += e.Delta;
            if (Math.Abs(_wheelRest) >= 120)
            {
                int dir = _wheelRest > 0 ? -1 : 1;
                _wheelRest = 0;
                if (Step != null) Step(dir);
            }
        }

        private void Pan(int dx, int dy)
        {
            float s = ViewScale;
            _center = new PointF(_center.X + dx / s, _center.Y + dy / s);
            Rough();
            Clamp();
            Invalidate();
        }

        private const int WM_MOUSEHWHEEL = 0x020E;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEHWHEEL)
            {
                int delta = (short)((long)m.WParam >> 16 & 0xFFFF);
                if (Pannable) Pan(delta, 0);
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _settle.Dispose(); _fade.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
