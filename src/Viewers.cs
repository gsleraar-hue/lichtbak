using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Lichtbak
{
    /// <summary>
    /// All windows of the one Lichtbak process. Decides where opened files go: into the window
    /// used last (the default), or each into a window of its own when that setting is on.
    /// </summary>
    /// <summary>Win+F3 from anywhere opens the overview.</summary>
    internal class HotkeyWindow : NativeWindow
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hwnd, int id, int modifiers, int key);

        private const int WM_HOTKEY = 0x0312, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
            if (!RegisterHotKey(Handle, 1, MOD_WIN | MOD_NOREPEAT, (int)Keys.F3))
                Log.Write("Win+F3 is taken by another program");
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY) Viewers.ToggleOverview();
            base.WndProc(ref m);
        }
    }

    internal static class Viewers
    {
        private static readonly List<ViewerForm> Windows = new List<ViewerForm>();
        private static ViewerForm _last;
        private static Control _sync;
        private static Overview _overview;
        private static HotkeyWindow _hotkey;

        /// <summary>Call once on the UI thread before anything else.</summary>
        public static void Init()
        {
            _sync = new Control();
            _sync.CreateControl();
            _hotkey = new HotkeyWindow();
        }

        /// <summary>Shows (or hides) the overview of all Lichtbak windows.</summary>
        public static void ToggleOverview()
        {
            if (_overview != null && !_overview.IsDisposed) { _overview.Close(); return; }
            if (Windows.Count == 0) return;
            _overview = new Overview(new List<ViewerForm>(Windows), _last);
            _overview.FormClosed += delegate { _overview = null; };
            _overview.Show();
        }

        /// <summary>Runs an action on the UI thread; safe from any thread.</summary>
        public static void Post(Action action)
        {
            _sync.BeginInvoke(action);
        }

        public static void Open(List<string> files)
        {
            if (files.Count == 0)
            {
                if (Windows.Count == 0) NewWindow(false).AddFiles(files, true);
                else (_last ?? Windows[Windows.Count - 1]).AddFiles(files, true);
                return;
            }

            if (!Settings.Shared.SeparateWindows)
            {
                ViewerForm target = _last ?? Windows.LastOrDefault() ?? NewWindow(false);
                target.AddFiles(files, true);
                return;
            }

            var sorted = new List<string>(files);
            sorted.Sort(Pictures.CompareNames);
            foreach (string path in sorted)
            {
                ViewerForm existing = Windows.FirstOrDefault(w => w.Contains(path));
                if (existing != null) { existing.AddFiles(new[] { path }, true); continue; }
                // A folder still opens as one window with everything in it.
                NewWindow(!Directory.Exists(path)).AddFiles(new[] { path }, true);
            }
        }

        public static void CloseAll()
        {
            foreach (ViewerForm w in Windows.ToArray()) w.Close();
        }

        private static ViewerForm NewWindow(bool single)
        {
            ViewerForm previous = _last ?? Windows.LastOrDefault();
            var form = new ViewerForm(single);
            if (previous != null && previous.WindowState == FormWindowState.Normal && form.WindowState == FormWindowState.Normal)
            {
                // Cascade, so a new window does not hide the one before it exactly.
                int step = SystemInformation.CaptionHeight + 8;
                var bounds = new Rectangle(previous.Left + step, previous.Top + step, previous.Width, previous.Height);
                Rectangle work = Screen.FromControl(previous).WorkingArea;
                if (bounds.Right > work.Right || bounds.Bottom > work.Bottom) bounds.Location = new Point(work.X + step, work.Y + step);
                form.Bounds = bounds;
            }
            form.Activated += delegate { _last = form; };
            form.FormClosed += delegate
            {
                Windows.Remove(form);
                if (_last == form) _last = Windows.LastOrDefault();
                if (Windows.Count == 0) Application.ExitThread();
            };
            Windows.Add(form);
            Log.Write("window " + Windows.Count + (single ? " (single)" : ""));
            _last = form;
            form.Show();
            return form;
        }
    }
}
