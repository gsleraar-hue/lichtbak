using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Lichtbak
{
    /// <summary>
    /// "Lichtbak.exe --selftest outdir file..." opens the files in an off-screen window, waits
    /// for them to load and renders the window into PNGs plus a text report. No screen capture.
    /// </summary>
    internal static class SelfTest
    {
        public static int Run(string[] args)
        {
            string outDir = args[1];
            var files = new List<string>();
            for (int i = 2; i < args.Length; i++) files.Add(Path.GetFullPath(args[i]));
            Directory.CreateDirectory(outDir);
            var report = new StringBuilder();

            Application.EnableVisualStyles();
            var form = new ViewerForm { ShowInTaskbar = false };
            form.Load += delegate
            {
                form.WindowState = FormWindowState.Normal;
                form.Bounds = new Rectangle(-4000, -4000, 1200, 800);
                form.AddFiles(files, false);
            };

            int step = 0;
            DateTime started = DateTime.Now;
            var timer = new Timer { Interval = 100 };
            timer.Tick += delegate
            {
                if (!form.TestReady() && (DateTime.Now - started).TotalSeconds < 15) return;
                started = DateTime.Now;
                switch (step++)
                {
                    case 0:
                        report.AppendLine(form.TestDescribe());
                        Render(form, outDir, "1-first.png", report);
                        form.TestKey(Keys.Right);
                        break;
                    case 1:
                        Render(form, outDir, "2-next.png", report);
                        form.TestKey(Keys.Control | Keys.D1);
                        break;
                    case 2:
                        Render(form, outDir, "3-actual-size.png", report);
                        form.TestKey(Keys.End);
                        break;
                    case 3:
                        Render(form, outDir, "4-last.png", report);
                        form.TestKey(Keys.Delete);
                        break;
                    default:
                        report.AppendLine("after Delete:");
                        report.AppendLine(form.TestDescribe());
                        File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToString());
                        timer.Stop();
                        form.Close();
                        break;
                }
            };
            form.Shown += delegate { timer.Start(); };
            Application.Run(form);
            return 0;
        }

        /// <summary>"--selftest-setup out.png": renders the setup window, before and after installing.</summary>
        public static int RenderSetup(string path)
        {
            Application.EnableVisualStyles();
            var form = new SetupForm { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000) };
            form.Shown += delegate
            {
                Render(form, Path.GetDirectoryName(path), Path.GetFileName(path), new StringBuilder());
                form.TestDone();
                Render(form, Path.GetDirectoryName(path), "after-" + Path.GetFileName(path), new StringBuilder());
                form.Close();
            };
            Application.Run(form);
            return 0;
        }

        private static void Render(Form form, string dir, string name, StringBuilder report)
        {
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                bitmap.Save(Path.Combine(dir, name), ImageFormat.Png);
            }
            report.AppendLine(name + ": title=\"" + form.Text + "\"");
        }
    }
}
