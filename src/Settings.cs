using System;
using System.Drawing;
using System.IO;

namespace Lichtbak
{
    /// <summary>Window position and sidebar, in %APPDATA%\Lichtbak\settings.ini.</summary>
    internal class Settings
    {
        public Rectangle Bounds;
        public bool Maximized;
        public bool Sidebar = true;
        public int SidebarWidth = 170;
        public bool SeparateWindows;

        /// <summary>One copy for all windows of the process.</summary>
        public static readonly Settings Shared = Load();

        private static string FilePath { get { return Path.Combine(Log.Folder, "settings.ini"); } }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "window":
                            string[] p = value.Split(',');
                            if (p.Length == 4) s.Bounds = new Rectangle(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]));
                            break;
                        case "maximized": s.Maximized = value == "1"; break;
                        case "sidebar": s.Sidebar = value != "0"; break;
                        case "sidebarwidth": s.SidebarWidth = int.Parse(value); break;
                        case "separatewindows": s.SeparateWindows = value == "1"; break;
                    }
                }
            }
            catch (Exception e) { Log.Write("reading settings: " + e.Message); }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Log.Folder);
                File.WriteAllLines(FilePath, new[]
                {
                    "window=" + Bounds.X + "," + Bounds.Y + "," + Bounds.Width + "," + Bounds.Height,
                    "maximized=" + (Maximized ? "1" : "0"),
                    "sidebar=" + (Sidebar ? "1" : "0"),
                    "sidebarwidth=" + SidebarWidth,
                    "separatewindows=" + (SeparateWindows ? "1" : "0"),
                });
            }
            catch (Exception e) { Log.Write("saving settings: " + e.Message); }
        }
    }
}
