using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Lichtbak
{
    /// <summary>One opened file. Bitmaps are only assigned and disposed on the UI thread.</summary>
    internal class Picture
    {
        public readonly string Path;
        public readonly string Name;
        public Bitmap Thumb, Preview, Full;
        public Size FullSize;
        public string Error;
        public bool ThumbBusy, PreviewBusy, FullBusy;
        /// <summary>Position in the list, kept up to date by the window; read by the thumbnail thread.</summary>
        public volatile int Index;
        public bool Removed;

        public Picture(string path)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path);
        }

        public void DropBitmaps()
        {
            if (Thumb != null) { Thumb.Dispose(); Thumb = null; }
            DropLarge();
        }

        public void DropLarge()
        {
            if (Preview != null) { Preview.Dispose(); Preview = null; }
            if (Full != null) { Full.Dispose(); Full = null; }
        }
    }

    /// <summary>
    /// Two background threads: one makes thumbnails (visible ones first), one decodes the
    /// picture on screen plus its neighbours. Results come back on the UI thread.
    /// </summary>
    internal class Loader
    {
        public const int ThumbSide = 240;

        private enum Kind { Preview, Full }

        private readonly Control _ui;
        private readonly Action<Picture> _done;
        private readonly object _gate = new object();
        private readonly List<Picture> _thumbs = new List<Picture>();
        private readonly List<KeyValuePair<Picture, Kind>> _large = new List<KeyValuePair<Picture, Kind>>();
        private readonly AutoResetEvent _thumbSignal = new AutoResetEvent(false);
        private readonly AutoResetEvent _largeSignal = new AutoResetEvent(false);
        private readonly int _previewSide;

        /// <summary>Thumbnails nearest to this list index go first.</summary>
        public volatile int Focus;

        public Loader(Control ui, Action<Picture> done)
        {
            _ui = ui;
            _done = done;
            int side = 1280;
            foreach (Screen s in Screen.AllScreens) side = Math.Max(side, Math.Max(s.Bounds.Width, s.Bounds.Height));
            _previewSide = side;
            Start(ThumbLoop, "thumbs");
            Start(LargeLoop, "large");
        }

        public int PreviewSide { get { return _previewSide; } }

        private static void Start(ThreadStart loop, string name)
        {
            new Thread(loop) { IsBackground = true, Name = name, Priority = ThreadPriority.BelowNormal }.Start();
        }

        // ------------------------------------------------------------- called on the UI thread

        public void QueueThumbs(IEnumerable<Picture> pictures)
        {
            lock (_gate)
            {
                foreach (Picture p in pictures)
                {
                    if (p.Thumb != null || p.ThumbBusy) continue;
                    p.ThumbBusy = true;
                    _thumbs.Add(p);
                }
            }
            _thumbSignal.Set();
        }

        public void ForgetThumb(Picture p)
        {
            lock (_gate) { _thumbs.Remove(p); }
        }

        /// <summary>Replaces the queue of large decodes: first the current picture, then its neighbours.</summary>
        public void Want(IEnumerable<Picture> previews, Picture full)
        {
            lock (_gate)
            {
                foreach (var pending in _large)
                {
                    if (pending.Value == Kind.Preview) pending.Key.PreviewBusy = false;
                    else pending.Key.FullBusy = false;
                }
                _large.Clear();
                bool first = true;
                foreach (Picture p in previews)
                {
                    if (p.Preview == null && !p.PreviewBusy && p.Error == null)
                    {
                        p.PreviewBusy = true;
                        _large.Add(new KeyValuePair<Picture, Kind>(p, Kind.Preview));
                    }
                    if (first && full != null && full.Full == null && !full.FullBusy && full.Error == null)
                    {
                        full.FullBusy = true;
                        _large.Add(new KeyValuePair<Picture, Kind>(full, Kind.Full));
                    }
                    first = false;
                }
            }
            _largeSignal.Set();
        }

        // ------------------------------------------------------------- worker threads

        private void ThumbLoop()
        {
            while (true)
            {
                Picture next = null;
                lock (_gate)
                {
                    if (_thumbs.Count > 0)
                    {
                        // Closest to what the sidebar is showing.
                        int best = 0, bestDistance = int.MaxValue, focus = Focus;
                        for (int i = 0; i < _thumbs.Count; i++)
                        {
                            int d = Math.Abs(_thumbs[i].Index - focus);
                            if (d < bestDistance) { bestDistance = d; best = i; }
                        }
                        next = _thumbs[best];
                        _thumbs.RemoveAt(best);
                    }
                }
                if (next == null) { _thumbSignal.WaitOne(); continue; }

                Bitmap thumb = null; Size size = Size.Empty; string error = null;
                try { thumb = Pictures.Load(next.Path, ThumbSide, out size); }
                catch (Exception e) { error = e.Message; }
                Picture p = next;
                Post(delegate
                {
                    p.ThumbBusy = false;
                    if (p.Removed) { if (thumb != null) thumb.Dispose(); return; }
                    if (thumb != null) { p.Thumb = thumb; p.FullSize = size; }
                    else if (p.Error == null) p.Error = error;
                    _done(p);
                });
            }
        }

        private void LargeLoop()
        {
            while (true)
            {
                KeyValuePair<Picture, Kind> job;
                lock (_gate)
                {
                    if (_large.Count == 0) job = new KeyValuePair<Picture, Kind>(null, Kind.Preview);
                    else { job = _large[0]; _large.RemoveAt(0); }
                }
                if (job.Key == null) { _largeSignal.WaitOne(); continue; }

                Picture p = job.Key;
                Kind kind = job.Value;
                Bitmap bitmap = null; Size size = Size.Empty; string error = null;
                try { bitmap = Pictures.Load(p.Path, kind == Kind.Full ? 0 : _previewSide, out size); }
                catch (OutOfMemoryException) { error = Strings.T("Too large to open.", "Te groot om te openen."); }
                catch (Exception e) { error = e.Message; }
                Post(delegate
                {
                    if (p.Removed) { if (bitmap != null) bitmap.Dispose(); return; }
                    if (kind == Kind.Full)
                    {
                        p.FullBusy = false;
                        if (bitmap != null) p.Full = bitmap;
                    }
                    else
                    {
                        p.PreviewBusy = false;
                        if (bitmap != null)
                        {
                            p.Preview = bitmap;
                            p.FullSize = size;
                        }
                        else p.Error = error;
                    }
                    if (bitmap == null && error != null) Log.Write("cannot open: " + p.Path + ": " + error);
                    _done(p);
                });
            }
        }

        private void Post(MethodInvoker action)
        {
            try { if (_ui.IsHandleCreated) _ui.BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }
    }
}
