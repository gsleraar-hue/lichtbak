using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GdiFormat = System.Drawing.Imaging.PixelFormat;

namespace Lichtbak
{
    /// <summary>
    /// Decoding goes through WIC (the codecs Windows itself uses), so WebP, HEIC and AVIF work
    /// whenever their Windows extension is installed. EXIF rotation is applied.
    /// </summary>
    internal static class Pictures
    {
        public static readonly string[] Extensions =
        {
            ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".bmp", ".dib", ".tif", ".tiff",
            ".ico", ".webp", ".heic", ".heif", ".avif", ".jxr", ".wdp"
        };

        public static bool IsPicture(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return Array.IndexOf(Extensions, ext) >= 0;
        }

        public static string DialogFilter
        {
            get
            {
                var parts = new List<string>();
                foreach (string e in Extensions) parts.Add("*" + e);
                return Strings.T("Pictures", "Afbeeldingen") + "|" + string.Join(";", parts.ToArray()) + "|" +
                       Strings.T("All files", "Alle bestanden") + "|*.*";
            }
        }

        /// <summary>
        /// Decodes a file. maxSide limits the longest side (0 = full size); fullSize receives the
        /// size of the whole picture after rotation.
        /// </summary>
        public static Bitmap Load(string path, int maxSide, out Size fullSize)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                BitmapFrame frame = decoder.Frames[0];
                int w = frame.PixelWidth, h = frame.PixelHeight;
                int orientation = Orientation(frame);
                bool turned = orientation >= 5 && orientation <= 8;
                fullSize = turned ? new Size(h, w) : new Size(w, h);

                stream.Position = 0;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                image.StreamSource = stream;
                if (maxSide > 0 && Math.Max(w, h) > maxSide)
                {
                    if (w >= h) image.DecodePixelWidth = maxSide;
                    else image.DecodePixelHeight = maxSide;
                }
                image.EndInit();

                BitmapSource source = image;
                if (source.Format != PixelFormats.Pbgra32)
                    source = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);

                var bitmap = new Bitmap(source.PixelWidth, source.PixelHeight, GdiFormat.Format32bppPArgb);
                var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                                           System.Drawing.Imaging.ImageLockMode.WriteOnly, GdiFormat.Format32bppPArgb);
                try
                {
                    source.CopyPixels(System.Windows.Int32Rect.Empty, data.Scan0, data.Stride * data.Height, data.Stride);
                }
                finally { bitmap.UnlockBits(data); }

                RotateFlipType flip = Flip(orientation);
                if (flip != RotateFlipType.RotateNoneFlipNone) bitmap.RotateFlip(flip);
                return bitmap;
            }
        }

        private static int Orientation(BitmapFrame frame)
        {
            try
            {
                var meta = frame.Metadata as BitmapMetadata;
                if (meta == null) return 1;
                object value = meta.GetQuery("System.Photo.Orientation");
                if (value is ushort) return (ushort)value;
                if (value is int) return (int)value;
            }
            catch { }
            return 1;
        }

        private static RotateFlipType Flip(int orientation)
        {
            switch (orientation)
            {
                case 2: return RotateFlipType.RotateNoneFlipX;
                case 3: return RotateFlipType.Rotate180FlipNone;
                case 4: return RotateFlipType.RotateNoneFlipY;
                case 5: return RotateFlipType.Rotate90FlipX;
                case 6: return RotateFlipType.Rotate90FlipNone;
                case 7: return RotateFlipType.Rotate270FlipX;
                case 8: return RotateFlipType.Rotate270FlipNone;
                default: return RotateFlipType.RotateNoneFlipNone;
            }
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string a, string b);

        /// <summary>Sorts names the way Explorer does (photo2 before photo10).</summary>
        public static int CompareNames(string a, string b)
        {
            return StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b));
        }
    }
}
