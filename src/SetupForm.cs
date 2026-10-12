using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Lichtbak
{
    /// <summary>
    /// Shown when the exe is called Lichtbak-setup.exe (the release download): one window that
    /// installs and then offers to make Lichtbak the default app for pictures.
    /// </summary>
    internal class SetupForm : Form
    {
        private readonly Label _title = new Label();
        private readonly Label _text = new Label();
        private readonly ModernButton _primary = new ModernButton { Primary = true };
        private readonly ModernButton _secondary = new ModernButton();
        private readonly ProgressLine _progress = new ProgressLine();
        private bool _installed;

        public SetupForm()
        {
            float dpi;
            using (Graphics g = CreateGraphics()) dpi = g.DpiX / 96f;
            Func<float, int> d = v => (int)Math.Round(v * dpi);

            Text = Strings.T("Install Lichtbak", "Lichtbak installeren");
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(d(500), d(250));
            BackColor = Modern.Back;
            ForeColor = Modern.Text;

            _title.Font = Modern.Semibold(15f);
            _title.AutoSize = false;
            _title.SetBounds(d(28), d(24), d(444), d(36));
            _title.Text = "Lichtbak";

            _text.Font = Modern.Font(10f, FontStyle.Regular);
            _text.ForeColor = Modern.SubText;
            _text.AutoSize = false;
            _text.SetBounds(d(28), d(66), d(444), d(100));
            _text.Text = Strings.T(
                "All your open pictures together in one window, with thumbnails in a sidebar, like Preview on the Mac.\n\nInstalls for you only; no administrator rights needed.",
                "Al je geopende afbeeldingen samen in één venster, met miniaturen in een zijbalk, zoals Voorvertoning op de Mac.\n\nInstalleert alleen voor jou; geen beheerdersrechten nodig.");

            var footer = new Panel { Dock = DockStyle.Bottom, Height = d(64), BackColor = Modern.Footer };
            _primary.Font = _secondary.Font = Modern.Font(10f, FontStyle.Regular);
            _primary.SetBounds(d(500 - 28 - 150 - 8 - 110), d(16), d(150), d(32));
            _secondary.SetBounds(d(500 - 28 - 110), d(16), d(110), d(32));
            _primary.Text = Strings.T("Install", "Installeren");
            _secondary.Text = Strings.T("Cancel", "Annuleren");
            _primary.Click += delegate { if (_installed) OpenDefaults(); else Install(); };
            _secondary.Click += delegate { Close(); };
            footer.Controls.Add(_primary);
            footer.Controls.Add(_secondary);

            _progress.SetBounds(d(28), d(170), d(444), d(4));
            _progress.Running = false;

            Controls.Add(_title);
            Controls.Add(_text);
            Controls.Add(_progress);
            Controls.Add(footer);
            AcceptButton = null;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Modern.StyleTitleBar(Handle, Modern.Back);
        }

        private void Install()
        {
            _primary.Enabled = _secondary.Enabled = false;
            _progress.Running = true;
            new Thread(() =>
            {
                int result = Lichtbak.Install.Run();
                BeginInvoke((MethodInvoker)delegate { Done(result == 0); });
            }) { IsBackground = true }.Start();
        }

        private void Done(bool ok)
        {
            _progress.Running = false;
            _primary.Enabled = _secondary.Enabled = true;
            _secondary.Text = Strings.T("Close", "Sluiten");
            if (!ok)
            {
                _title.Text = Strings.T("Installation failed", "Installeren is mislukt");
                _text.Text = Strings.T("Details are in ", "Details staan in ") + "%APPDATA%\\Lichtbak\\lichtbak.log.";
                _primary.Visible = false;
                return;
            }
            _installed = true;
            _title.Text = Strings.T("Lichtbak is installed", "Lichtbak is geïnstalleerd");
            _text.Text = Strings.T(
                "Select pictures in Explorer and choose Open with > Lichtbak: they all open in one window.\n\nTo open pictures with Enter or a double-click, make Lichtbak your default app for pictures.",
                "Selecteer afbeeldingen in Verkenner en kies Openen met > Lichtbak: ze openen samen in één venster.\n\nWil je ze openen met Enter of dubbelklikken, maak Lichtbak dan je standaard-app voor afbeeldingen.");
            _primary.Text = Strings.T("Set as default…", "Als standaard instellen…");
        }

        internal void TestDone() { Done(true); }

        private void OpenDefaults()
        {
            try { Process.Start("ms-settings:defaultapps?registeredAppUser=Lichtbak"); }
            catch { Process.Start("ms-settings:defaultapps"); }
        }
    }
}
