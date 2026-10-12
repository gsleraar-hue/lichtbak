using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

// Draws the Lichtbak icon (a glowing light table with two photos on it) and writes a
// multi-size .ico.
internal static class MakeIcon
{
    private static readonly int[] Sizes = { 256, 64, 48, 32, 24, 16 };

    private static void Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "lichtbak.ico";
        var pngs = new byte[Sizes.Length][];
        for (int i = 0; i < Sizes.Length; i++) pngs[i] = DrawPng(Sizes[i]);

        using (var file = new FileStream(output, FileMode.Create, FileAccess.Write))
        using (var w = new BinaryWriter(file))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)Sizes.Length);
            int offset = 6 + 16 * Sizes.Length;
            for (int i = 0; i < Sizes.Length; i++)
            {
                w.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i]));
                w.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i]));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(pngs[i].Length); w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (byte[] png in pngs) w.Write(png);
        }
        Console.WriteLine("written: " + Path.GetFullPath(output));
        if (args.Length > 1) File.WriteAllBytes(args[1], DrawPng(512));
    }

    private static byte[] DrawPng(int size)
    {
        using (var bitmap = new Bitmap(size, size))
        {
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float s = size;
                bool tiny = size <= 24;

                // The light table.
                var table = tiny ? new RectangleF(0.5f, s * 0.08f, s - 1, s * 0.84f)
                                 : new RectangleF(s * 0.06f, s * 0.12f, s * 0.88f, s * 0.76f);
                using (GraphicsPath path = Round(table, s * 0.14f))
                {
                    using (var glow = new LinearGradientBrush(table, Color.FromArgb(255, 252, 236), Color.FromArgb(255, 205, 110), 90f))
                        g.FillPath(glow, path);
                    using (var rim = new Pen(Color.FromArgb(200, 214, 140, 40), Math.Max(1f, s / 48f)))
                        g.DrawPath(rim, path);
                }

                if (tiny)
                {
                    Photo(g, new RectangleF(s * 0.2f, s * 0.24f, s * 0.6f, s * 0.52f), 0f, s);
                }
                else
                {
                    Photo(g, new RectangleF(s * 0.17f, s * 0.24f, s * 0.42f, s * 0.36f), -8f, s);
                    Photo(g, new RectangleF(s * 0.42f, s * 0.38f, s * 0.42f, s * 0.36f), 6f, s);
                }
            }
            using (var memory = new MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                return memory.ToArray();
            }
        }
    }

    /// <summary>A small photo: white border, sky, sun and a green hill.</summary>
    private static void Photo(Graphics g, RectangleF r, float angle, float s)
    {
        GraphicsState state = g.Save();
        g.TranslateTransform(r.X + r.Width / 2, r.Y + r.Height / 2);
        g.RotateTransform(angle);
        var frame = new RectangleF(-r.Width / 2, -r.Height / 2, r.Width, r.Height);

        using (var shadow = new SolidBrush(Color.FromArgb(60, 120, 70, 0)))
            g.FillRectangle(shadow, frame.X + s * 0.012f, frame.Y + s * 0.02f, frame.Width, frame.Height);
        g.FillRectangle(Brushes.White, frame);

        float border = Math.Max(1f, r.Width * 0.08f);
        var picture = RectangleF.Inflate(frame, -border, -border);
        using (var sky = new LinearGradientBrush(picture, Color.FromArgb(120, 190, 255), Color.FromArgb(200, 230, 255), 90f))
            g.FillRectangle(sky, picture);
        g.SetClip(picture);
        float sun = picture.Height * 0.32f;
        using (var yellow = new SolidBrush(Color.FromArgb(255, 200, 40)))
            g.FillEllipse(yellow, picture.Right - sun * 1.5f, picture.Y + sun * 0.35f, sun, sun);
        using (var hill = new SolidBrush(Color.FromArgb(70, 170, 90)))
            g.FillEllipse(hill, picture.X - picture.Width * 0.3f, picture.Y + picture.Height * 0.55f, picture.Width * 1.1f, picture.Height);
        using (var hill2 = new SolidBrush(Color.FromArgb(40, 135, 75)))
            g.FillEllipse(hill2, picture.X + picture.Width * 0.35f, picture.Y + picture.Height * 0.68f, picture.Width, picture.Height);
        g.ResetClip();
        g.Restore(state);
    }

    private static GraphicsPath Round(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
