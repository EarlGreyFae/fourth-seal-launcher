// The launcher window. Press Play: it sets everything up (Installer.cs)
// and opens the Minecraft Launcher with The Fourth Seal profile ready.
//
// "--selftest" runs the same steps with no window (for tools/launcher/test).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace FourthSealLauncher
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Settings.Load();
            if (Array.IndexOf(args, "--selftest") >= 0) return SelfTest();
            // Development only: write the .exe's icon file (build.cmd uses it).
            int ico = Array.IndexOf(args, "--write-icon");
            if (ico >= 0 && ico + 1 < args.Length)
            {
                ProfileIcon.WriteIco(args[ico + 1]);
                return 0;
            }
            // Development only: save a picture of the window to check its layout.
            int shot = Array.IndexOf(args, "--screenshot");
            if (shot >= 0 && shot + 1 < args.Length)
            {
                Application.EnableVisualStyles();
                using (var form = new MainForm())
                {
                    form.Show();
                    Application.DoEvents();
                    using (var bmp = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                        bmp.Save(args[shot + 1], System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                return 0;
            }

            bool first;
            using (var mutex = new Mutex(true, "TheFourthSealLauncher", out first))
            {
                if (!first)
                {
                    MessageBox.Show("The Fourth Seal launcher is already open.", "The Fourth Seal Skyblock");
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            return 0;
        }

        // Headless run: log to the console and launcher.log; never waits for
        // anyone. Exit code 0 = everything worked.
        static int SelfTest()
        {
            var log = new LogFile();
            var installer = new Installer();
            installer.Log = log.Write;
            installer.Status = s => log.Write("[status] " + s);
            installer.AskCloseLauncher = () => false;
            installer.AskManual = list =>
            {
                foreach (var m in list) log.Write("[manual] " + m.FileName + " <- " + m.PageUrl);
                return false;
            };
            try
            {
                installer.Run();
                log.Write("[done] " + installer.VersionId);
                return 0;
            }
            catch (Exception e)
            {
                log.Write("[error] " + e.Message);
                log.Write(e.ToString());
                return 1;
            }
        }
    }

    // Everything the launcher says also goes to launcher.log (for support).
    class LogFile
    {
        readonly object gate = new object();
        readonly string path;

        public LogFile()
        {
            Directory.CreateDirectory(Settings.HomeDir);
            path = Path.Combine(Settings.HomeDir, "launcher.log");
            try { File.WriteAllText(path, "The Fourth Seal launcher - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine); }
            catch (Exception) { }
        }

        public void Write(string line)
        {
            lock (gate)
            {
                Console.WriteLine(line);
                try { File.AppendAllText(path, line + Environment.NewLine); }
                catch (Exception) { }
            }
        }
    }

    class MainForm : Form
    {
        static readonly Color Purple = Color.FromArgb(46, 26, 71);
        static readonly Color DeepPurple = Color.FromArgb(30, 16, 48);
        static readonly Color Gold = Color.FromArgb(214, 170, 72);

        readonly Label status = new Label();
        readonly ProgressBar progress = new ProgressBar();
        readonly TextBox logBox = new TextBox();
        readonly Button play = new Button();
        readonly Button folder = new Button();
        readonly LogFile log = new LogFile();
        bool busy;

        public MainForm()
        {
            Text = "The Fourth Seal Skyblock";
            ClientSize = new Size(720, 470);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Purple;
            Font = new Font("Segoe UI", 9.5f);
            using (var bmp = ProfileIcon.Draw(64)) Icon = Icon.FromHandle(bmp.GetHicon());

            var emblem = new PictureBox();
            emblem.Image = ProfileIcon.Draw(72);
            emblem.SetBounds(24, 20, 72, 72);
            Controls.Add(emblem);

            var title = new Label();
            title.Text = "The Fourth Seal Skyblock";
            title.Font = new Font("Georgia", 22f, FontStyle.Bold);
            title.ForeColor = Gold;
            title.AutoSize = true;
            title.Location = new Point(110, 24);
            Controls.Add(title);

            var subtitle = new Label();
            subtitle.Text = "All the Mods 9 - To the Sky  •  " + Settings.ServerAddress;
            subtitle.ForeColor = Color.Gainsboro;
            subtitle.AutoSize = true;
            subtitle.Location = new Point(114, 66);
            Controls.Add(subtitle);

            status.Text = "Press Play to get the modpack ready and open Minecraft.";
            status.ForeColor = Color.White;
            status.Font = new Font("Segoe UI", 11f);
            status.SetBounds(24, 110, 672, 26);
            Controls.Add(status);

            progress.SetBounds(24, 140, 672, 14);
            Controls.Add(progress);

            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.BackColor = DeepPurple;
            logBox.ForeColor = Color.Gainsboro;
            logBox.BorderStyle = BorderStyle.None;
            logBox.Font = new Font("Consolas", 9f);
            logBox.SetBounds(24, 166, 672, 220);
            Controls.Add(logBox);

            play.Text = "Play";
            play.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
            play.BackColor = Gold;
            play.ForeColor = DeepPurple;
            play.FlatStyle = FlatStyle.Flat;
            play.FlatAppearance.BorderSize = 0;
            play.SetBounds(516, 404, 180, 46);
            play.Click += (s, e) => Start();
            Controls.Add(play);

            folder.Text = "Open game folder";
            folder.ForeColor = Color.White;
            folder.FlatStyle = FlatStyle.Flat;
            folder.SetBounds(24, 412, 150, 32);
            folder.Click += (s, e) =>
            {
                Directory.CreateDirectory(Settings.GameDir);
                Process.Start("explorer.exe", "\"" + Settings.GameDir + "\"");
            };
            Controls.Add(folder);

            AcceptButton = play;
            FormClosing += (s, e) =>
            {
                if (busy && MessageBox.Show(this, "The launcher is still working. Stop and close it?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes)
                    e.Cancel = true;
            };
        }

        void Start()
        {
            if (busy) return;
            busy = true;
            play.Enabled = false;
            logBox.Clear();
            status.ForeColor = Color.White;
            var installer = new Installer();
            installer.Log = line => { log.Write(line); Ui(() => logBox.AppendText(line + Environment.NewLine)); };
            installer.Status = text => { log.Write("[status] " + text); Ui(() => status.Text = text); };
            installer.Progress = (value, max) => Ui(() =>
            {
                progress.Maximum = Math.Max(1, max);
                progress.Value = Math.Min(progress.Maximum, Math.Max(0, value));
            });
            installer.AskCloseLauncher = () => (bool)Invoke(new Func<bool>(() =>
                MessageBox.Show(this, "The Minecraft Launcher is open.\n\nPlease close it (it would undo the new profile when it closes), then press Retry.", Text, MessageBoxButtons.RetryCancel, MessageBoxIcon.Information) == DialogResult.Retry));
            installer.AskManual = list => (bool)Invoke(new Func<bool>(() =>
            {
                using (var dialog = new ManualDownloadsForm(list)) return dialog.ShowDialog(this) == DialogResult.Retry;
            }));

            var worker = new Thread(() =>
            {
                try
                {
                    installer.Run();
                    Ui(() =>
                    {
                        status.ForeColor = Gold;
                        status.Text = Settings.NoLaunch ? "Ready!" : "Ready! In the Minecraft Launcher, press Play - " + Settings.ServerName + " is selected.";
                        progress.Value = progress.Maximum;
                    });
                }
                catch (Exception e)
                {
                    log.Write("[error] " + e);
                    Ui(() =>
                    {
                        status.ForeColor = Color.FromArgb(255, 140, 140);
                        status.Text = "Something went wrong - see below.";
                        logBox.AppendText(Environment.NewLine + e.Message + Environment.NewLine + "(Full details: " + Path.Combine(Settings.HomeDir, "launcher.log") + ")" + Environment.NewLine);
                    });
                }
                finally
                {
                    Ui(() => { busy = false; play.Enabled = true; play.Text = "Play"; });
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        void Ui(Action action)
        {
            if (IsDisposed) return;
            try { BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }
    }

    // Mods CurseForge only allows through its website: open each page,
    // download, then "Check again" (they're picked up from Downloads).
    class ManualDownloadsForm : Form
    {
        public ManualDownloadsForm(List<ManualFile> files)
        {
            Text = "A few mods to download yourself";
            ClientSize = new Size(560, 420);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9.5f);

            var intro = new Label();
            intro.Text = "These mods' authors only allow downloads from the CurseForge website. Click each one, press Download on its page (it goes to your Downloads folder - don't rename it), then press Check again.";
            intro.SetBounds(16, 12, 528, 54);
            Controls.Add(intro);

            var panel = new FlowLayoutPanel();
            panel.FlowDirection = FlowDirection.TopDown;
            panel.WrapContents = false;
            panel.AutoScroll = true;
            panel.SetBounds(16, 70, 528, 290);
            foreach (var f in files)
            {
                var link = new LinkLabel();
                link.Text = f.Name + "  (" + f.FileName + ")";
                link.AutoSize = true;
                link.Margin = new Padding(0, 4, 0, 4);
                var url = f.PageUrl;
                link.LinkClicked += (s, e) => Process.Start(url);
                panel.Controls.Add(link);
            }
            Controls.Add(panel);

            var openAll = new Button();
            openAll.Text = "Open all pages";
            openAll.SetBounds(16, 374, 140, 32);
            openAll.Click += (s, e) => { foreach (var f in files) Process.Start(f.PageUrl); };
            Controls.Add(openAll);

            var check = new Button();
            check.Text = "Check again";
            check.DialogResult = DialogResult.Retry;
            check.SetBounds(298, 374, 120, 32);
            Controls.Add(check);

            var cancel = new Button();
            cancel.Text = "Cancel";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(424, 374, 120, 32);
            Controls.Add(cancel);

            AcceptButton = check;
            CancelButton = cancel;
        }
    }
}
