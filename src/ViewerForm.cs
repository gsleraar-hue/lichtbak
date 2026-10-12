using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Lichtbak
{
    internal class ViewerForm : Form
    {
        private readonly List<Picture> _items = new List<Picture>();
        private readonly ThumbStrip _strip = new ThumbStrip();
        private readonly Control _grip = new Grip();
        private readonly ImageView _view = new ImageView();
        private readonly Loader _loader;
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private readonly Settings _settings = Settings.Shared;
        private readonly bool _single;
        private int _current = -1;

        // Files that arrive within a moment of each other (Explorer starting us once per file)
        // form one batch, which is sorted by name and shown from its first picture.
        private int _batchStart = -1;
        private DateTime _lastArrival = DateTime.MinValue;

        private bool _flashedSize;
        private bool _fullScreen;
        private FormWindowState _savedState;
        private Rectangle _savedBounds;
        private bool _savedSidebar;

        public ViewerForm() : this(false) { }

        /// <summary>single: a window for one picture (the "own window" setting); starts without sidebar.</summary>
        public ViewerForm(bool single)
        {
            _single = single;
            Text = Program.Name;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            KeyPreview = true;
            AllowDrop = true;
            StartPosition = FormStartPosition.Manual;

            float dpi;
            using (Graphics g = CreateGraphics()) dpi = g.DpiX / 96f;
            MinimumSize = new Size((int)(420 * dpi), (int)(300 * dpi));
            Rectangle bounds = _settings.Bounds;
            if (bounds.Width < 200 || !Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds)))
            {
                Rectangle work = Screen.PrimaryScreen.WorkingArea;
                bounds = new Rectangle(0, 0, (int)(work.Width * 0.7), (int)(work.Height * 0.8));
                bounds.Offset(work.X + (work.Width - bounds.Width) / 2, work.Y + (work.Height - bounds.Height) / 2);
            }
            Bounds = bounds;
            if (_settings.Maximized) WindowState = FormWindowState.Maximized;

            _strip.Dock = DockStyle.Left;
            _strip.Width = Math.Max((int)(110 * dpi), (int)(_settings.SidebarWidth * dpi));
            _strip.Visible = _settings.Sidebar && !single;
            _grip.Dock = DockStyle.Left;
            _grip.Width = Math.Max(4, (int)(5 * dpi));
            _grip.Visible = _strip.Visible;
            _view.Dock = DockStyle.Fill;
            _view.EmptyText = Strings.T("Drop pictures here or press Ctrl+O", "Sleep afbeeldingen hierheen of druk op Ctrl+O");
            Controls.Add(_view);
            Controls.Add(_grip);
            Controls.Add(_strip);

            _loader = new Loader(this, Loaded);
            _strip.Items = _items;
            _strip.Picked += ShowAt;
            _strip.MenuRequested += (i, p) => _menu.Show(_strip, p);
            _strip.Moved += Reorder;
            _strip.FilesDropped += files => AddFiles(files, false);
            _strip.Scroll += delegate { _loader.Focus = (_strip.FirstVisible + _strip.LastVisible) / 2; };
            _strip.MouseWheel += delegate { _loader.Focus = (_strip.FirstVisible + _strip.LastVisible) / 2; };
            _view.Step += dir => Go(_current + dir);
            _view.NeedFull += RequestLarge;
            _view.ZoomChanged += delegate { Flash(); };
            _view.ContextMenuStrip = _menu;
            _view.SidebarToggle += ToggleSidebar;
            _view.SidebarShown = _strip.Visible;
            _grip.MouseMove += GripMove;

            BuildMenu();
            ApplyTheme();
            SystemEvents.UserPreferenceChanged += ThemeChanged;
        }

        // ------------------------------------------------------------- files

        public void AddFiles(IEnumerable<string> paths, bool activate)
        {
            var incoming = new List<string>();
            foreach (string path in paths)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        var inFolder = Directory.GetFiles(path).Where(Pictures.IsPicture).ToList();
                        inFolder.Sort(Pictures.CompareNames);
                        incoming.AddRange(inFolder);
                    }
                    else if (File.Exists(path)) incoming.Add(Path.GetFullPath(path));
                }
                catch (Exception e) { Log.Write("adding: " + path + ": " + e.Message); }
            }

            Picture show = null;
            var added = new List<Picture>();
            DateTime now = DateTime.Now;
            if (_batchStart < 0 || _batchStart > _items.Count || (now - _lastArrival).TotalMilliseconds > 1500)
                _batchStart = _items.Count;
            _lastArrival = now;

            foreach (string path in incoming)
            {
                Picture existing = _items.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase))
                                   ?? added.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
                if (existing != null) { if (show == null) show = existing; continue; }
                added.Add(new Picture(path));
            }

            if (added.Count > 0)
            {
                _items.AddRange(added);
                var batch = _items.GetRange(_batchStart, _items.Count - _batchStart);
                batch.Sort((a, b) => Pictures.CompareNames(a.Path, b.Path));
                Picture keep = _current >= 0 && _current < _batchStart ? _items[_current] : null;
                _items.RemoveRange(_batchStart, batch.Count);
                _items.AddRange(batch);
                Renumber();
                _strip.Relayout();
                _loader.QueueThumbs(added);
                show = _items[_batchStart];
                if (keep != null) _current = keep.Index;
            }

            Log.Write("received " + incoming.Count + " file(s), list now " + _items.Count);
            if (show != null) ShowAt(show.Index);
            else if (_items.Count == 0) ShowAt(-1);
            if (activate) BringForward();
        }

        /// <summary>The picture on screen, for the overview.</summary>
        internal Bitmap CurrentBitmap
        {
            get
            {
                if (_current < 0) return null;
                Picture p = _items[_current];
                return p.Preview ?? p.Full ?? p.Thumb;
            }
        }

        internal string CurrentName { get { return _current < 0 ? Program.Name : _items[_current].Name; } }

        public bool Contains(string path)
        {
            return _items.Any(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
        }

        private void Renumber()
        {
            for (int i = 0; i < _items.Count; i++) _items[i].Index = i;
        }

        private void ShowAt(int index)
        {
            if (index >= _items.Count) index = _items.Count - 1;
            Picture before = _view.Picture;
            _current = index;
            Picture now = index >= 0 ? _items[index] : null;
            _strip.Selected = index;
            if (now != before) _view.Show(now);
            _strip.EnsureVisible(index);
            _strip.Invalidate();
            _loader.Focus = index;
            UpdateTitle();
            RequestLarge();
            Evict();
            Flash();
        }

        private void Go(int index)
        {
            if (_items.Count == 0) return;
            ShowAt(Math.Max(0, Math.Min(_items.Count - 1, index)));
        }

        private void RequestLarge()
        {
            if (_current < 0) { _loader.Want(new Picture[0], null); return; }
            var order = new List<Picture> { _items[_current] };
            if (_current + 1 < _items.Count) order.Add(_items[_current + 1]);
            if (_current - 1 >= 0) order.Add(_items[_current - 1]);
            _loader.Want(order, _view.WantsFull ? _items[_current] : null);
        }

        /// <summary>Keeps large bitmaps only for the current picture and its neighbours.</summary>
        private void Evict()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                Picture p = _items[i];
                if (Math.Abs(i - _current) > 2 && (p.Preview != null || p.Full != null)) p.DropLarge();
                else if (i != _current && p.Full != null) { p.Full.Dispose(); p.Full = null; }
            }
        }

        private void Loaded(Picture p)
        {
            if (p.Index == _current && _current >= 0 && _items[_current] == p)
            {
                _view.Invalidate();
                UpdateTitle();
                if (!_flashedSize && !p.FullSize.IsEmpty) Flash();
                if (_view.WantsFull) RequestLarge();
            }
            int first = _strip.FirstVisible, last = _strip.LastVisible;
            if (p.Index >= first && p.Index <= last) _strip.Invalidate();
        }

        private void Remove(int index)
        {
            if (index < 0 || index >= _items.Count) return;
            Picture p = _items[index];
            p.Removed = true;
            _loader.ForgetThumb(p);
            _items.RemoveAt(index);
            if (_view.Picture == p) _view.Show(null);
            p.DropBitmaps();
            Renumber();
            _batchStart = -1;
            _strip.Relayout();
            _current = -1;
            ShowAt(Math.Min(index, _items.Count - 1));
        }

        private void Reorder(int from, int to)
        {
            Picture p = _items[from];
            Picture current = _current >= 0 ? _items[_current] : null;
            _items.RemoveAt(from);
            _items.Insert(to, p);
            Renumber();
            _batchStart = -1;
            _current = current == null ? -1 : current.Index;
            _strip.Selected = _current;
            _strip.Invalidate();
            UpdateTitle();
        }

        private void Recycle(int index)
        {
            if (index < 0 || index >= _items.Count) return;
            string path = _items[index].Path;
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e)
            {
                MessageBox.Show(this, Strings.T("Could not move the file to the Recycle Bin.", "Kon het bestand niet naar de prullenbak verplaatsen.") + "\n" + e.Message, Program.Name);
                return;
            }
            Remove(index);
        }

        // ------------------------------------------------------------- title and overlay

        private void UpdateTitle()
        {
            if (_current < 0) { Text = Program.Name; return; }
            Text = _items[_current].Name + (_items.Count > 1 ? "  (" + Of() + ")" : "");
        }

        private string Of()
        {
            return (_current + 1) + Strings.T(" of ", " van ") + _items.Count;
        }

        private void Flash()
        {
            if (_current < 0) return;
            Picture p = _items[_current];
            _flashedSize = !p.FullSize.IsEmpty;
            var parts = new List<string>();
            if (_items.Count > 1) parts.Add(Of());
            if (!p.FullSize.IsEmpty) parts.Add(p.FullSize.Width + " × " + p.FullSize.Height);
            parts.Add((int)Math.Round(_view.ViewScale * 100) + "%");
            _view.Flash(string.Join("   ·   ", parts.ToArray()));
        }

        // ------------------------------------------------------------- commands

        private ToolStripMenuItem _sidebarItem, _separateItem;

        private void BuildMenu()
        {
            Modern.Apply(_menu);
            Add(Strings.T("Show in Explorer", "Weergeven in Verkenner"), null, delegate { ShowInExplorer(); });
            Add(Strings.T("Open with…", "Openen met…"), null, delegate { OpenWith(); });
            Add(Strings.T("Copy", "Kopiëren"), "Ctrl+C", delegate { CopyCurrent(); });
            _menu.Items.Add(new ToolStripSeparator());
            Add(Strings.T("Fit to window", "Passend maken"), "Ctrl+0", delegate { _view.Fit(); });
            Add(Strings.T("Actual size", "Ware grootte"), "Ctrl+1", delegate { _view.ActualSize(null); });
            Add(Strings.T("Zoom in", "Inzoomen"), "Ctrl++", delegate { _view.ZoomBy(1.25f, null); });
            Add(Strings.T("Zoom out", "Uitzoomen"), "Ctrl+-", delegate { _view.ZoomBy(0.8f, null); });
            Add(Strings.T("Full screen", "Volledig scherm"), "F11", delegate { ToggleFullScreen(); }).Tag = "always";
            Add(Strings.T("All Lichtbak windows", "Alle Lichtbak-vensters"), "F3", delegate { Viewers.ToggleOverview(); }).Tag = "always";
            _sidebarItem = Add(Strings.T("Sidebar", "Zijbalk"), "Tab", delegate { ToggleSidebar(); });
            _separateItem = Add(Strings.T("Open each picture in its own window", "Elke afbeelding in een eigen venster"), null, delegate
            {
                _settings.SeparateWindows = !_settings.SeparateWindows;
                _settings.Save();
            });
            _separateItem.Tag = "always";
            _menu.Items.Add(new ToolStripSeparator());
            Add(Strings.T("Add pictures…", "Afbeeldingen toevoegen…"), "Ctrl+O", delegate { OpenDialog(); }).Tag = "always";
            Add(Strings.T("Remove from list", "Uit de lijst halen"), "Del", delegate { Remove(_current); });
            Add(Strings.T("Move to Recycle Bin", "Naar de prullenbak"), "Ctrl+Del", delegate { Recycle(_current); });
            _menu.Opening += (s, e) =>
            {
                bool any = _current >= 0;
                foreach (ToolStripItem item in _menu.Items)
                    if (item is ToolStripMenuItem && item != _sidebarItem && (string)item.Tag != "always")
                        item.Enabled = any;
                _sidebarItem.Checked = _strip.Visible;
                _separateItem.Checked = _settings.SeparateWindows;
            };
        }

        private ToolStripMenuItem Add(string text, string keys, EventHandler click)
        {
            var item = new ToolStripMenuItem(text, null, click);
            if (keys != null) item.ShortcutKeyDisplayString = keys;
            _menu.Items.Add(item);
            return item;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Right: case Keys.Down: case Keys.PageDown: case Keys.Space: Go(_current + 1); return true;
                case Keys.Left: case Keys.Up: case Keys.PageUp: case Keys.Back: Go(_current - 1); return true;
                case Keys.Home: Go(0); return true;
                case Keys.End: Go(_items.Count - 1); return true;
                case Keys.Control | Keys.Oemplus: case Keys.Control | Keys.Add: case Keys.Control | Keys.Shift | Keys.Oemplus:
                    _view.ZoomBy(1.25f, null); return true;
                case Keys.Control | Keys.OemMinus: case Keys.Control | Keys.Subtract: _view.ZoomBy(0.8f, null); return true;
                case Keys.Control | Keys.D0: case Keys.Control | Keys.NumPad0: _view.Fit(); return true;
                case Keys.Control | Keys.D1: case Keys.Control | Keys.NumPad1: _view.ActualSize(null); return true;
                case Keys.F11: ToggleFullScreen(); return true;
                case Keys.F3: Viewers.ToggleOverview(); return true;
                case Keys.Escape: if (_fullScreen) ToggleFullScreen(); return true;
                case Keys.Tab: ToggleSidebar(); return true;
                case Keys.Delete: Remove(_current); return true;
                case Keys.Control | Keys.Delete: Recycle(_current); return true;
                case Keys.Control | Keys.C: CopyCurrent(); return true;
                case Keys.Control | Keys.O: OpenDialog(); return true;
                case Keys.Control | Keys.W: Close(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void OpenDialog()
        {
            using (var dialog = new OpenFileDialog { Multiselect = true, Filter = Pictures.DialogFilter, Title = Strings.T("Add pictures", "Afbeeldingen toevoegen") })
            {
                if (_current >= 0) dialog.InitialDirectory = Path.GetDirectoryName(_items[_current].Path);
                if (dialog.ShowDialog(this) == DialogResult.OK) AddFiles(dialog.FileNames, false);
            }
        }

        private void ShowInExplorer()
        {
            if (_current < 0) return;
            Process.Start("explorer.exe", "/select,\"" + _items[_current].Path + "\"");
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenAsInfo
        {
            public string File;
            public string Class;
            public int Flags;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHOpenWithDialog(IntPtr parent, ref OpenAsInfo info);

        private void OpenWith()
        {
            if (_current < 0) return;
            var info = new OpenAsInfo { File = _items[_current].Path, Flags = 0x1 | 0x4 };  // allow registration, run
            SHOpenWithDialog(Handle, ref info);
        }

        private void CopyCurrent()
        {
            if (_current < 0) return;
            Picture p = _items[_current];
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, new[] { p.Path });
            Bitmap image = p.Full ?? p.Preview;
            if (image != null) data.SetData(DataFormats.Bitmap, true, image);
            try { Clipboard.SetDataObject(data, true); } catch (ExternalException) { }
            _view.Flash(Strings.T("Copied", "Gekopieerd"));
        }

        private void ToggleSidebar()
        {
            bool show = !_strip.Visible;
            _strip.Visible = show;
            _grip.Visible = show;
            if (!_fullScreen && !_single) _settings.Sidebar = show;
            if (show) _strip.EnsureVisible(_current);
            _view.SidebarShown = show;
            _view.Invalidate();
        }

        private void ToggleFullScreen()
        {
            if (!_fullScreen)
            {
                _savedState = WindowState;
                _savedBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                _savedSidebar = _strip.Visible;
                _fullScreen = true;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                Bounds = Screen.FromControl(this).Bounds;
                _strip.Visible = _grip.Visible = false;
                _view.SidebarShown = false;
                _view.BackColor = Color.Black;
            }
            else
            {
                _fullScreen = false;
                FormBorderStyle = FormBorderStyle.Sizable;
                Bounds = _savedBounds;
                WindowState = _savedState;
                _strip.Visible = _grip.Visible = _savedSidebar;
                _view.SidebarShown = _savedSidebar;
                ApplyTheme();
            }
        }

        // ------------------------------------------------------------- window

        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);

        private void BringForward()
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            if (!Visible) Show();
            SetForegroundWindow(Handle);
            Activate();
        }

        private void GripMove(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int width = PointToClient(Cursor.Position).X - _grip.Width / 2;
            float dpi = DeviceDpi / 96f;
            _strip.Width = Math.Max((int)(110 * dpi), Math.Min((int)(420 * dpi), width));
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) && !e.Data.GetDataPresent("Lichtbak.Index")
                ? DragDropEffects.Copy : DragDropEffects.None;
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null) AddFiles(files.Where(f => Directory.Exists(f) || Pictures.IsPicture(f)), false);
        }

        private void ThemeChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General) return;
            BeginInvoke((MethodInvoker)delegate { Modern.Refresh(); ApplyTheme(); });
        }

        private void ApplyTheme()
        {
            BackColor = Modern.Back;
            _view.BackColor = _fullScreen ? Color.Black : (Modern.Dark ? Color.FromArgb(28, 28, 28) : Color.FromArgb(238, 238, 238));
            _strip.BackColor = Modern.Dark ? Color.FromArgb(38, 38, 38) : Color.FromArgb(246, 246, 246);
            _grip.BackColor = _strip.BackColor;
            _strip.ApplyTheme();
            _grip.Invalidate();
            if (IsHandleCreated) Modern.StyleTitleBar(Handle, _strip.BackColor);
            _menu.Renderer = new ModernMenuRenderer();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Modern.StyleTitleBar(Handle, _strip.BackColor);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            SystemEvents.UserPreferenceChanged -= ThemeChanged;
            if (_fullScreen) ToggleFullScreen();
            if (ShowInTaskbar == false) return;  // self-test window
            _settings.Maximized = WindowState == FormWindowState.Maximized;
            _settings.Bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _settings.SidebarWidth = (int)(_strip.Width / (DeviceDpi / 96f));
            _settings.Save();
        }

        // ------------------------------------------------------------- self-test hooks

        internal bool TestReady()
        {
            if (_current < 0) return false;
            foreach (Picture p in _items) if (p.ThumbBusy) return false;
            Picture c = _items[_current];
            if (c.Preview == null && c.Error == null) return false;
            return !_view.WantsFull || c.Full != null || c.Error != null;
        }

        internal string TestDescribe()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("current=" + _current + " of " + _items.Count + ", zoom=" + Math.Round(_view.ViewScale * 100) + "%");
            foreach (Picture p in _items)
                sb.AppendLine("  " + p.Index + " " + p.Name + "  size=" + p.FullSize.Width + "x" + p.FullSize.Height +
                    "  thumb=" + (p.Thumb == null ? "-" : p.Thumb.Width + "x" + p.Thumb.Height) +
                    "  preview=" + (p.Preview == null ? "-" : p.Preview.Width + "x" + p.Preview.Height) +
                    "  full=" + (p.Full == null ? "-" : p.Full.Width + "x" + p.Full.Height) +
                    (p.Error != null ? "  ERROR=" + p.Error : ""));
            return sb.ToString();
        }

        internal void TestKey(Keys keys)
        {
            var m = new Message();
            ProcessCmdKey(ref m, keys);
        }

        /// <summary>The handle between sidebar and picture; draws a hairline and resizes the sidebar.</summary>
        private class Grip : Control
        {
            public Grip()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                Cursor = Cursors.SizeWE;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);
                using (var pen = new Pen(Modern.Dark ? Color.FromArgb(20, 20, 20) : Color.FromArgb(222, 222, 222)))
                    e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);
            }
        }
    }
}
