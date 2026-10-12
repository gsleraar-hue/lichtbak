using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Media.Imaging;

// Writes test pictures for the self-test: a big landscape JPEG, a JPEG whose EXIF says
// "rotate 90 degrees", a transparent PNG and a file that is not a picture at all.
internal static class MakeSamples
{
    private static void Main(string[] args)
    {
        string dir = args[0];
        Directory.CreateDirectory(dir);
        Save(Draw(6000, 4000, "photo10 landscape", Color.SteelBlue), Path.Combine(dir, "photo10.jpg"), 1);
        Save(Draw(3000, 2000, "photo2 EXIF 6: should be portrait", Color.IndianRed), Path.Combine(dir, "photo2.jpg"), 6);
        using (var b = new Bitmap(500, 700))
        {
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(Brushes.Orange, 50, 100, 400, 500);
            }
            b.Save(Path.Combine(dir, "logo.png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        File.WriteAllText(Path.Combine(dir, "broken.jpg"), "not a picture");
    }

    private static Bitmap Draw(int w, int h, string text, Color color)
    {
        var b = new Bitmap(w, h);
        using (Graphics g = Graphics.FromImage(b))
        using (var brush = new LinearGradientBrush(new Rectangle(0, 0, w, h), color, Color.White, 30f))
        {
            g.FillRectangle(brush, 0, 0, w, h);
            g.FillRectangle(Brushes.Black, 0, 0, w / 10, h / 10);  // marks the top-left corner
            using (var font = new Font("Segoe UI", h / 12f))
                g.DrawString(text, font, Brushes.Black, w / 10f, h / 2.5f);
        }
        return b;
    }

    private static void Save(Bitmap bitmap, string path, int orientation)
    {
        var temp = Path.GetTempFileName();
        bitmap.Save(temp, System.Drawing.Imaging.ImageFormat.Png);
        bitmap.Dispose();
        using (var input = File.OpenRead(temp))
        {
            var frame = BitmapFrame.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var meta = new BitmapMetadata("jpg");
            meta.SetQuery("/app1/ifd/{ushort=274}", (ushort)orientation);
            var encoder = new JpegBitmapEncoder { QualityLevel = 88 };
            encoder.Frames.Add(BitmapFrame.Create(frame, null, meta, null));
            using (var output = File.Create(path)) encoder.Save(output);
        }
        File.Delete(temp);
    }
}
