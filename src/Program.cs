using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Lichtbak
{
    /// <summary>
    /// One process, one window. When Explorer starts Lichtbak once per selected file, every
    /// later process hands its files to the first one over a named pipe and quits, so a
    /// selection always ends up together in a single window, like Preview on the Mac.
    /// </summary>
    internal static class Program
    {
        public const string Name = "Lichtbak";

        private static string PipeName
        {
            get { return "Lichtbak-" + Environment.UserName + "-" + Process.GetCurrentProcess().SessionId; }
        }

        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "--install": return Install.Run();
                    case "--uninstall": return Install.Uninstall();
                    case "--quit": QuitRunning(); return 0;
                    case "--selftest-setup": SetProcessDPIAware(); return SelfTest.RenderSetup(args[1]);
                    case "--selftest": SetProcessDPIAware(); return SelfTest.Run(args);
                }
            }

            var files = new List<string>();
            foreach (string a in args)
            {
                if (a.StartsWith("--")) continue;
                try { files.Add(Path.GetFullPath(a)); } catch { }
            }
            Log.Write("start, " + files.Count + " file(s)");

            if (files.Count == 0 &&
                Path.GetFileNameWithoutExtension(Application.ExecutablePath).IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new SetupForm());
                return 0;
            }

            bool owner;
            var mutex = new Mutex(true, @"Local\Lichtbak.Instance", out owner);
            if (!owner)
            {
                if (Send(files, false)) return 0;
                // The other instance is closing down; wait for it and take over.
                try { owner = mutex.WaitOne(5000); } catch (AbandonedMutexException) { owner = true; }
                if (!owner) return 1;
            }

            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Viewers.Init();
            Viewers.Open(files);
            var server = new Thread(Serve) { IsBackground = true, Name = "pipe" };
            server.Start();
            Application.Run();
            GC.KeepAlive(mutex);
            return 0;
        }

        /// <summary>Asks the running instance to quit; used by the installer.</summary>
        public static void QuitRunning()
        {
            Send(new List<string>(), true);
        }

        private static bool Send(List<string> files, bool quit)
        {
            // Our process may still bring a window to the front (Explorer just started us);
            // pass that right on so the first instance can raise itself.
            AllowSetForegroundWindow(-1);
            var message = new StringBuilder();
            message.Append(quit ? "quit\n" : "open\n");
            foreach (string f in files) message.Append(f).Append('\n');
            byte[] bytes = Encoding.UTF8.GetBytes(message.ToString());

            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 5000)
            {
                try
                {
                    using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                    {
                        client.Connect(500);
                        client.Write(bytes, 0, bytes.Length);
                        client.Flush();
                        return true;
                    }
                }
                catch (TimeoutException) { }
                catch (IOException) { Thread.Sleep(50); }
            }
            return false;
        }

        private static void Serve()
        {
            while (true)
            {
                try
                {
                    using (var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1))
                    {
                        server.WaitForConnection();
                        string text;
                        using (var reader = new StreamReader(server, Encoding.UTF8)) text = reader.ReadToEnd();
                        string[] lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (lines.Length == 0) continue;
                        var files = new List<string>();
                        for (int i = 1; i < lines.Length; i++) files.Add(lines[i].TrimEnd('\r'));
                        bool quit = lines[0] == "quit";
                        Viewers.Post(delegate
                        {
                            if (quit) Viewers.CloseAll();
                            else Viewers.Open(files);
                        });
                    }
                }
                catch (Exception e)
                {
                    Log.Write("pipe: " + e.Message);
                    Thread.Sleep(200);
                }
            }
        }
    }

    internal static class Log
    {
        private static readonly object Gate = new object();

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lichtbak"); }
        }

        public static void Write(string line)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Folder);
                    string path = Path.Combine(Folder, "lichtbak.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 200000) File.Delete(path);
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" +
                        Process.GetCurrentProcess().Id + "] " + line + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
