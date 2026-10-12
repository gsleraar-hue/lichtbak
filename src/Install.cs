using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Lichtbak
{
    /// <summary>
    /// "Lichtbak.exe --install" copies itself to %LOCALAPPDATA%\Programs\Lichtbak and registers
    /// for all picture types: "Open with", Default apps in Settings, a right-click verb and a
    /// Start menu entry. Everything lives under HKCU; no admin rights needed.
    ///
    /// The verbs use MultiSelectModel=Player, so Explorer offers them for selections of any size
    /// (the default model hides a verb above 15 files).
    /// </summary>
    internal static class Install
    {
        private const string ProgId = "Lichtbak.Picture";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Lichtbak";

        private static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Lichtbak"); }
        }

        private static string ShortcutPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Lichtbak.lnk"); }
        }

        public static int Run()
        {
            try
            {
                string target = Path.Combine(Folder, "Lichtbak.exe");
                string self = Application.ExecutablePath;
                Directory.CreateDirectory(Folder);
                if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                {
                    StopRunning();
                    Retry(() => File.Copy(self, target, true));
                }
                Register(target);
                CreateShortcut(target);
                Log.Write("installed: " + target);
                return 0;
            }
            catch (Exception e)
            {
                Log.Write("install failed: " + e);
                return 1;
            }
        }

        public static int Uninstall()
        {
            try
            {
                StopRunning();
                Unregister();
                if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
                Log.Write("uninstalled");
                // The running exe cannot delete itself; let cmd do it once we have exited.
                Process.Start(new ProcessStartInfo("cmd.exe",
                    "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + Folder + "\"")
                { CreateNoWindow = true, UseShellExecute = false });
                MessageBox.Show(Strings.T("Lichtbak has been removed.", "Lichtbak is verwijderd."), "Lichtbak");
                return 0;
            }
            catch (Exception e)
            {
                Log.Write("uninstall failed: " + e);
                MessageBox.Show(Strings.T("Lichtbak could not be removed completely:", "Verwijderen is niet helemaal gelukt:") + "\n" + e.Message, "Lichtbak");
                return 1;
            }
        }

        private static void StopRunning()
        {
            Mutex existing;
            if (!Mutex.TryOpenExisting(@"Local\Lichtbak.Instance", out existing)) return;
            existing.Dispose();
            Program.QuitRunning();
            Thread.Sleep(800);
        }

        private static void Retry(Action action)
        {
            for (int i = 0; ; i++)
            {
                try { action(); return; }
                catch (IOException) { if (i >= 20) throw; Thread.Sleep(250); }
                catch (UnauthorizedAccessException) { if (i >= 20) throw; Thread.Sleep(250); }
            }
        }

        // ------------------------------------------------------------- registry

        private static void Register(string exe)
        {
            string command = "\"" + exe + "\" \"%1\"";
            string icon = "\"" + exe + "\",0";
            using (RegistryKey classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes"))
            {
                using (RegistryKey k = classes.CreateSubKey(ProgId))
                {
                    k.SetValue("", Strings.T("Picture", "Afbeelding"));
                    k.SetValue("FriendlyTypeName", Strings.T("Picture", "Afbeelding"));
                }
                using (RegistryKey k = classes.CreateSubKey(ProgId + @"\DefaultIcon")) k.SetValue("", icon);
                Verb(classes, ProgId + @"\shell\open", null, null, command);

                using (RegistryKey k = classes.CreateSubKey(@"Applications\Lichtbak.exe"))
                {
                    k.SetValue("FriendlyAppName", "Lichtbak");
                }
                Verb(classes, @"Applications\Lichtbak.exe\shell\open", null, null, command);
                using (RegistryKey k = classes.CreateSubKey(@"Applications\Lichtbak.exe\SupportedTypes"))
                    foreach (string ext in Pictures.Extensions) k.SetValue(ext, "");

                foreach (string ext in Pictures.Extensions)
                    using (RegistryKey k = classes.CreateSubKey(ext + @"\OpenWithProgids")) k.SetValue(ProgId, new byte[0], RegistryValueKind.None);

                // Right-click > Open in Lichtbak (under "Show more options" in Windows 11).
                Verb(classes, @"SystemFileAssociations\image\shell\Lichtbak", Strings.T("Open in Lichtbak", "Openen in Lichtbak"), icon, command);
            }

            using (RegistryKey caps = Registry.CurrentUser.CreateSubKey(@"Software\Lichtbak\Capabilities"))
            {
                caps.SetValue("ApplicationName", "Lichtbak");
                caps.SetValue("ApplicationDescription", Strings.T("All your open pictures together in one window, with thumbnails in a sidebar.", Strings.T("All your open pictures together in one window, with thumbnails in a sidebar.", Strings.T("All your open pictures together in one window, with thumbnails in a sidebar.", Strings.T("All your open pictures together in one window, with thumbnails in a sidebar.", "Al je geopende afbeeldingen samen in één venster, met miniaturen in een zijbalk.")))));
                caps.SetValue("ApplicationIcon", icon);
                using (RegistryKey assoc = caps.CreateSubKey("FileAssociations"))
                    foreach (string ext in Pictures.Extensions) assoc.SetValue(ext, ProgId);
            }
            using (RegistryKey apps = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
                apps.SetValue("Lichtbak", @"Software\Lichtbak\Capabilities");

            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "Lichtbak");
                k.SetValue("DisplayIcon", icon);
                k.SetValue("DisplayVersion", Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
                k.SetValue("Publisher", "Lichtbak");
                k.SetValue("InstallLocation", Folder);
                k.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
            }
            NotifyShell();
        }

        private static void Verb(RegistryKey classes, string path, string label, string icon, string command)
        {
            using (RegistryKey k = classes.CreateSubKey(path))
            {
                if (label != null) k.SetValue("MUIVerb", label);
                if (icon != null) k.SetValue("Icon", icon);
                k.SetValue("MultiSelectModel", "Player");
            }
            using (RegistryKey k = classes.CreateSubKey(path + @"\command")) k.SetValue("", command);
        }

        private static void Unregister()
        {
            using (RegistryKey classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes"))
            {
                classes.DeleteSubKeyTree(ProgId, false);
                classes.DeleteSubKeyTree(@"Applications\Lichtbak.exe", false);
                classes.DeleteSubKeyTree(@"SystemFileAssociations\image\shell\Lichtbak", false);
                foreach (string ext in Pictures.Extensions)
                    using (RegistryKey k = classes.OpenSubKey(ext + @"\OpenWithProgids", true))
                        if (k != null) k.DeleteValue(ProgId, false);
            }
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Lichtbak", false);
            using (RegistryKey apps = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", true))
                if (apps != null) apps.DeleteValue("Lichtbak", false);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            NotifyShell();
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

        private static void NotifyShell()
        {
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);  // SHCNE_ASSOCCHANGED
        }

        private static void CreateShortcut(string exe)
        {
            Type type = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(type);
            try
            {
                object link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { ShortcutPath });
                Type lt = link.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { exe });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Folder });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { Strings.T("View pictures", "Afbeeldingen bekijken") });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                Marshal.ReleaseComObject(link);
            }
            finally { Marshal.ReleaseComObject(shell); }
        }
    }
}
