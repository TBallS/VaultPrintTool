using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VaultPrintToolInstaller
{
    internal static class Program
    {
        private const string AppName = "Vault Print Tool";
        private const string AppVersion = "3.0.0";
        private const string Publisher = "Vault Print Tool";
        private const string ExeName = "VaultPrintTool_v3.exe";
        private const string UninstallerName = "Uninstall.exe";
        private const string ZipResourceName = "VaultPrintTool_Release_v3.0.zip";
        private const string IconResourceName = "icon.ico";
        private const string UninstallRegSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\VaultPrintTool";

        [STAThread]
        private static int Main(string[] args)
        {
            bool silent = false;
            bool uninstall = false;
            string customDir = null;

            string currentExeName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
            if (string.Equals(currentExeName, UninstallerName, StringComparison.OrdinalIgnoreCase))
            {
                uninstall = true;
            }

            foreach (string rawArg in args)
            {
                if (string.IsNullOrWhiteSpace(rawArg)) continue;
                string arg = rawArg.Trim();

                if (arg.Equals("/S", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("-S", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--silent", StringComparison.OrdinalIgnoreCase))
                {
                    silent = true;
                }
                else if (arg.Equals("/uninstall", StringComparison.OrdinalIgnoreCase) ||
                         arg.Equals("-uninstall", StringComparison.OrdinalIgnoreCase) ||
                         arg.Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    uninstall = true;
                }
                else if (arg.StartsWith("/D=", StringComparison.OrdinalIgnoreCase) ||
                         arg.StartsWith("-D=", StringComparison.OrdinalIgnoreCase))
                {
                    customDir = arg.Substring(3).Trim().Trim('"');
                }
            }

            if (silent)
            {
                try
                {
                    if (uninstall)
                    {
                        PerformUninstall(null);
                    }
                    else
                    {
                        string targetDir = string.IsNullOrWhiteSpace(customDir)
                            ? GetDefaultInstallDir()
                            : Path.GetFullPath(customDir);
                        PerformInstall(targetDir, true, true, null);
                    }
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Error: " + ex.Message);
                    return 1;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                if (uninstall)
                {
                    Application.Run(new UninstallForm());
                }
                else
                {
                    string initialDir = string.IsNullOrWhiteSpace(customDir)
                        ? GetDefaultInstallDir()
                        : Path.GetFullPath(customDir);
                    Application.Run(new InstallerForm(initialDir));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Setup encountered an unexpected error:\r\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            return 0;
        }

        internal static string GetDefaultInstallDir()
        {
            string localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }
            return Path.Combine(localAppData, @"Programs\VaultPrintTool");
        }

        internal static string GetStartMenuProgramsDir()
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (!string.IsNullOrWhiteSpace(dir)) return dir;

            string appData = Environment.GetEnvironmentVariable("APPDATA");
            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }
            return string.IsNullOrWhiteSpace(appData)
                ? null
                : Path.Combine(appData, @"Microsoft\Windows\Start Menu\Programs");
        }

        internal static string GetDesktopDir()
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrWhiteSpace(dir)) return dir;

            string userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            return string.IsNullOrWhiteSpace(userProfile)
                ? null
                : Path.Combine(userProfile, "Desktop");
        }

        internal static Icon LoadAppIcon()
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(IconResourceName))
                {
                    if (s != null)
                    {
                        return new Icon(s);
                    }
                }
            }
            catch { }

            try
            {
                return Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        internal static Image LoadAppBitmap(Icon appIcon)
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon.png"))
                {
                    if (s != null)
                    {
                        using (var img = Image.FromStream(s))
                        {
                            return new Bitmap(img);
                        }
                    }
                }
            }
            catch { }

            try
            {
                if (appIcon != null)
                {
                    using (var sized = new Icon(appIcon, 48, 48))
                    {
                        return sized.ToBitmap();
                    }
                }
            }
            catch { }

            return SystemIcons.Application.ToBitmap();
        }

        internal static void PerformInstall(
            string installDir,
            bool createDesktopShortcut,
            bool createStartMenuShortcut,
            Action<int, string> reportProgress)
        {
            installDir = Path.GetFullPath(installDir.Trim());
            if (string.IsNullOrEmpty(installDir))
            {
                throw new ArgumentException("Install directory cannot be empty.");
            }

            if (reportProgress != null) reportProgress(5, "Creating installation directory...");
            Directory.CreateDirectory(installDir);

            long totalBytes = 0;
            using (Stream zipStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ZipResourceName))
            {
                if (zipStream == null)
                {
                    throw new InvalidOperationException("Embedded release archive not found: " + ZipResourceName);
                }

                using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
                {
                    int total = archive.Entries.Count;
                    int index = 0;
                    string fullInstallRoot = installDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                           + Path.DirectorySeparatorChar;

                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        index++;
                        string relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                        string destPath = Path.GetFullPath(Path.Combine(installDir, relativePath));

                        if (!destPath.StartsWith(fullInstallRoot, StringComparison.OrdinalIgnoreCase) &&
                            !destPath.Equals(installDir, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException("Invalid archive entry path: " + entry.FullName);
                        }

                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(destPath);
                        }
                        else
                        {
                            string parentDir = Path.GetDirectoryName(destPath);
                            if (!string.IsNullOrEmpty(parentDir))
                            {
                                Directory.CreateDirectory(parentDir);
                            }

                            if (reportProgress != null)
                            {
                                int pct = 5 + (int)((index / (double)Math.Max(1, total)) * 80.0);
                                reportProgress(pct, "Extracting " + entry.Name + "...");
                            }

                            entry.ExtractToFile(destPath, true);
                            totalBytes += entry.Length;
                        }
                    }
                }
            }

            if (reportProgress != null) reportProgress(88, "Installing uninstaller...");
            string selfPath = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
            string uninstallerPath = Path.Combine(installDir, UninstallerName);
            if (!string.Equals(selfPath, uninstallerPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(selfPath, uninstallerPath, true);
            }

            string exePath = Path.Combine(installDir, ExeName);

            if (createStartMenuShortcut)
            {
                string programsDir = GetStartMenuProgramsDir();
                if (!string.IsNullOrWhiteSpace(programsDir))
                {
                    if (reportProgress != null) reportProgress(92, "Creating Start Menu shortcuts...");
                    try
                    {
                        string startMenuDir = Path.Combine(programsDir, AppName);
                        Directory.CreateDirectory(startMenuDir);

                        CreateShortcut(
                            Path.Combine(startMenuDir, AppName + ".lnk"),
                            exePath,
                            null,
                            installDir,
                            exePath + ",0",
                            "Launch " + AppName + " v" + AppVersion);

                        CreateShortcut(
                            Path.Combine(startMenuDir, "Uninstall " + AppName + ".lnk"),
                            uninstallerPath,
                            "/uninstall",
                            installDir,
                            uninstallerPath + ",0",
                            "Uninstall " + AppName);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("Warning: Could not create Start Menu shortcuts: " + ex.Message);
                    }
                }
            }

            if (createDesktopShortcut)
            {
                string desktopDir = GetDesktopDir();
                if (!string.IsNullOrWhiteSpace(desktopDir))
                {
                    if (reportProgress != null) reportProgress(95, "Creating Desktop shortcut...");
                    try
                    {
                        Directory.CreateDirectory(desktopDir);
                        CreateShortcut(
                            Path.Combine(desktopDir, AppName + ".lnk"),
                            exePath,
                            null,
                            installDir,
                            exePath + ",0",
                            "Launch " + AppName + " v" + AppVersion);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("Warning: Could not create Desktop shortcut: " + ex.Message);
                    }
                }
            }

            if (reportProgress != null) reportProgress(98, "Registering application...");
            int estimatedSizeKb = (int)Math.Max(1, totalBytes / 1024);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallRegSubKey))
            {
                if (key != null)
                {
                    key.SetValue("DisplayName", AppName, RegistryValueKind.String);
                    key.SetValue("DisplayVersion", AppVersion, RegistryValueKind.String);
                    key.SetValue("Publisher", Publisher, RegistryValueKind.String);
                    key.SetValue("DisplayIcon", exePath + ",0", RegistryValueKind.String);
                    key.SetValue("InstallLocation", installDir, RegistryValueKind.String);
                    key.SetValue("UninstallString", "\"" + uninstallerPath + "\" /uninstall", RegistryValueKind.String);
                    key.SetValue("QuietUninstallString", "\"" + uninstallerPath + "\" /uninstall /S", RegistryValueKind.String);
                    key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    key.SetValue("EstimatedSize", estimatedSizeKb, RegistryValueKind.DWord);
                }
            }

            if (reportProgress != null) reportProgress(100, "Installation complete.");
        }

        internal static void PerformUninstall(Action<int, string> reportProgress)
        {
            if (reportProgress != null) reportProgress(10, "Locating installation...");
            string installDir = null;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UninstallRegSubKey))
                {
                    if (key != null)
                    {
                        installDir = key.GetValue("InstallLocation") as string;
                    }
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(installDir))
            {
                string currentExe = Assembly.GetExecutingAssembly().Location;
                if (string.Equals(Path.GetFileName(currentExe), UninstallerName, StringComparison.OrdinalIgnoreCase))
                {
                    installDir = Path.GetDirectoryName(currentExe);
                }
                else
                {
                    installDir = GetDefaultInstallDir();
                }
            }

            if (reportProgress != null) reportProgress(30, "Removing shortcuts...");
            try
            {
                string desktopDir = GetDesktopDir();
                if (!string.IsNullOrWhiteSpace(desktopDir))
                {
                    string desktopShortcut = Path.Combine(desktopDir, AppName + ".lnk");
                    if (File.Exists(desktopShortcut))
                    {
                        File.Delete(desktopShortcut);
                    }
                }
            }
            catch { }

            try
            {
                string programsDir = GetStartMenuProgramsDir();
                if (!string.IsNullOrWhiteSpace(programsDir))
                {
                    string startMenuDir = Path.Combine(programsDir, AppName);
                    if (Directory.Exists(startMenuDir))
                    {
                        Directory.Delete(startMenuDir, true);
                    }
                }
            }
            catch { }

            if (reportProgress != null) reportProgress(60, "Removing registry entries...");
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(UninstallRegSubKey, false);
            }
            catch { }

            if (reportProgress != null) reportProgress(85, "Removing application files...");
            if (!string.IsNullOrWhiteSpace(installDir) && Directory.Exists(installDir))
            {
                string fullInstallDir = Path.GetFullPath(installDir)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string selfPath = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
                bool runningFromInstallDir = selfPath.StartsWith(
                    fullInstallDir + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);

                if (!runningFromInstallDir)
                {
                    try
                    {
                        Directory.Delete(fullInstallDir, true);
                    }
                    catch
                    {
                        ScheduleFolderDelete(fullInstallDir);
                    }
                }
                else
                {
                    ScheduleFolderDelete(fullInstallDir);
                }
            }

            if (reportProgress != null) reportProgress(100, "Uninstallation complete.");
        }

        private static void ScheduleFolderDelete(string folderPath)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + folderPath + "\"",
                WorkingDirectory = Path.GetTempPath(),
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
        }

        private static void CreateShortcut(
            string shortcutPath,
            string targetPath,
            string arguments,
            string workingDirectory,
            string iconLocation,
            string description)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                throw new InvalidOperationException("WScript.Shell is not available on this system.");
            }

            object shell = null;
            object shortcut = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { shortcutPath });

                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
                if (!string.IsNullOrEmpty(arguments))
                {
                    shortcutType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { arguments });
                }
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
                shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { iconLocation });
                shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }

        internal static void LaunchInstalledApp(string installDir)
        {
            string exePath = Path.Combine(installDir, ExeName);
            if (File.Exists(exePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = installDir,
                    UseShellExecute = true
                });
            }
        }
    }

    internal sealed class InstallerForm : Form
    {
        private readonly Panel _headerPanel;
        private readonly Label _headerTitle;
        private readonly Label _headerSubtitle;
        private readonly PictureBox _headerIcon;

        private readonly Panel _welcomePanel;
        private readonly TextBox _pathBox;
        private readonly Button _browseButton;
        private readonly CheckBox _desktopCheck;
        private readonly CheckBox _startMenuCheck;

        private readonly Panel _progressPanel;
        private readonly Label _progressLabel;
        private readonly ProgressBar _progressBar;

        private readonly Panel _finishPanel;
        private readonly CheckBox _launchCheck;

        private readonly Button _primaryButton;
        private readonly Button _cancelButton;

        private enum Page { Welcome, Installing, Finish }
        private Page _currentPage = Page.Welcome;
        private string _installedDir;

        public InstallerForm(string initialDir)
        {
            Icon appIcon = Program.LoadAppIcon();

            Text = "Vault Print Tool v3.0 Setup";
            Icon = appIcon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(500, 350);
            BackColor = SystemColors.Control;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            // Header
            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 68,
                BackColor = Color.White
            };
            _headerIcon = new PictureBox
            {
                Location = new Point(16, 10),
                Size = new Size(48, 48),
                SizeMode = PictureBoxSizeMode.StretchImage,
                Image = Program.LoadAppBitmap(appIcon)
            };
            _headerTitle = new Label
            {
                Location = new Point(76, 12),
                Size = new Size(400, 24),
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Text = "Vault Print Tool v3.0 Setup"
            };
            _headerSubtitle = new Label
            {
                Location = new Point(76, 38),
                Size = new Size(400, 18),
                ForeColor = Color.DimGray,
                Text = "Autodesk Vault Professional Utility"
            };
            var headerDivider = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = Color.LightGray
            };
            _headerPanel.Controls.AddRange(new Control[] { _headerIcon, _headerTitle, _headerSubtitle, headerDivider });

            // Footer
            var footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 52
            };
            var footerDivider = new Label
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = Color.LightGray
            };
            _primaryButton = new Button
            {
                Location = new Point(312, 13),
                Size = new Size(84, 26),
                Text = "&Install"
            };
            _primaryButton.Click += OnPrimaryClick;
            _cancelButton = new Button
            {
                Location = new Point(404, 13),
                Size = new Size(84, 26),
                Text = "Cancel"
            };
            _cancelButton.Click += (s, e) => Close();
            footerPanel.Controls.AddRange(new Control[] { footerDivider, _primaryButton, _cancelButton });
            AcceptButton = _primaryButton;
            CancelButton = _cancelButton;

            // Welcome Page
            _welcomePanel = new Panel
            {
                Location = new Point(0, 68),
                Size = new Size(500, 230)
            };
            var introLabel = new Label
            {
                Location = new Point(20, 16),
                Size = new Size(460, 38),
                Text = "Setup will install Vault Print Tool v3.0 on your computer. Choose an installation folder and shortcut options below, then click Install."
            };
            var destLabel = new Label
            {
                Location = new Point(20, 66),
                Size = new Size(460, 18),
                Text = "Destination Folder:"
            };
            _pathBox = new TextBox
            {
                Location = new Point(20, 88),
                Size = new Size(370, 23),
                Text = initialDir
            };
            _browseButton = new Button
            {
                Location = new Point(398, 86),
                Size = new Size(82, 26),
                Text = "Browse..."
            };
            _browseButton.Click += OnBrowseClick;
            _desktopCheck = new CheckBox
            {
                Location = new Point(20, 132),
                Size = new Size(300, 22),
                Checked = true,
                Text = "Create Desktop shortcut"
            };
            _startMenuCheck = new CheckBox
            {
                Location = new Point(20, 160),
                Size = new Size(300, 22),
                Checked = true,
                Text = "Create Start Menu shortcuts"
            };
            var perUserNote = new Label
            {
                Location = new Point(20, 196),
                Size = new Size(460, 18),
                ForeColor = Color.DimGray,
                Text = "Per-user installation (no administrator privileges required)."
            };
            _welcomePanel.Controls.AddRange(new Control[]
            {
                introLabel, destLabel, _pathBox, _browseButton, _desktopCheck, _startMenuCheck, perUserNote
            });

            // Progress Page
            _progressPanel = new Panel
            {
                Location = new Point(0, 68),
                Size = new Size(500, 230),
                Visible = false
            };
            _progressLabel = new Label
            {
                Location = new Point(20, 72),
                Size = new Size(460, 20),
                Text = "Preparing installation..."
            };
            _progressBar = new ProgressBar
            {
                Location = new Point(20, 98),
                Size = new Size(460, 24),
                Minimum = 0,
                Maximum = 100
            };
            _progressPanel.Controls.AddRange(new Control[] { _progressLabel, _progressBar });

            // Finish Page
            _finishPanel = new Panel
            {
                Location = new Point(0, 68),
                Size = new Size(500, 230),
                Visible = false
            };
            var finishLabel = new Label
            {
                Location = new Point(20, 24),
                Size = new Size(460, 40),
                Text = "Vault Print Tool v3.0 has been installed successfully.\r\n\r\nClick Finish to close Setup."
            };
            _launchCheck = new CheckBox
            {
                Location = new Point(20, 84),
                Size = new Size(300, 24),
                Checked = true,
                Text = "Launch Vault Print Tool"
            };
            _finishPanel.Controls.AddRange(new Control[] { finishLabel, _launchCheck });

            Controls.AddRange(new Control[] { _welcomePanel, _progressPanel, _finishPanel, footerPanel, _headerPanel });
        }

        private void OnBrowseClick(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select installation folder for Vault Print Tool v3.0";
                dlg.SelectedPath = Directory.Exists(_pathBox.Text) ? _pathBox.Text : Program.GetDefaultInstallDir();
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _pathBox.Text = dlg.SelectedPath;
                }
            }
        }

        private void OnPrimaryClick(object sender, EventArgs e)
        {
            if (_currentPage == Page.Welcome)
            {
                string targetDir = _pathBox.Text.Trim();
                if (string.IsNullOrEmpty(targetDir))
                {
                    MessageBox.Show(this, "Please specify a destination folder.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                bool desktop = _desktopCheck.Checked;
                bool startMenu = _startMenuCheck.Checked;
                _installedDir = targetDir;

                _currentPage = Page.Installing;
                _welcomePanel.Visible = false;
                _progressPanel.Visible = true;
                _primaryButton.Enabled = false;
                _cancelButton.Enabled = false;
                _headerSubtitle.Text = "Installing Vault Print Tool v3.0...";

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        Program.PerformInstall(targetDir, desktop, startMenu, (pct, msg) =>
                        {
                            if (!IsDisposed)
                            {
                                BeginInvoke((Action)(() =>
                                {
                                    _progressBar.Value = Math.Max(0, Math.Min(100, pct));
                                    _progressLabel.Text = msg;
                                }));
                            }
                        });

                        if (!IsDisposed)
                        {
                            BeginInvoke((Action)(() =>
                            {
                                _currentPage = Page.Finish;
                                _progressPanel.Visible = false;
                                _finishPanel.Visible = true;
                                _headerSubtitle.Text = "Completing Vault Print Tool v3.0 Setup";
                                _primaryButton.Text = "&Finish";
                                _primaryButton.Enabled = true;
                                _cancelButton.Visible = false;
                            }));
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!IsDisposed)
                        {
                            BeginInvoke((Action)(() =>
                            {
                                MessageBox.Show(this, "Installation failed:\r\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                                _currentPage = Page.Welcome;
                                _progressPanel.Visible = false;
                                _welcomePanel.Visible = true;
                                _primaryButton.Enabled = true;
                                _cancelButton.Enabled = true;
                                _headerSubtitle.Text = "Autodesk Vault Professional Utility";
                            }));
                        }
                    }
                });
            }
            else if (_currentPage == Page.Finish)
            {
                if (_launchCheck.Checked && !string.IsNullOrEmpty(_installedDir))
                {
                    try
                    {
                        Program.LaunchInstalledApp(_installedDir);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Could not launch Vault Print Tool:\r\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                Close();
            }
        }
    }

    internal sealed class UninstallForm : Form
    {
        private readonly Label _statusLabel;
        private readonly ProgressBar _progressBar;
        private readonly Button _uninstallButton;
        private readonly Button _cancelButton;
        private bool _completed;

        public UninstallForm()
        {
            Icon appIcon = Program.LoadAppIcon();

            Text = "Vault Print Tool v3.0 Uninstall";
            Icon = appIcon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(460, 220);
            BackColor = SystemColors.Control;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 60,
                BackColor = Color.White
            };
            var headerIcon = new PictureBox
            {
                Location = new Point(16, 10),
                Size = new Size(40, 40),
                SizeMode = PictureBoxSizeMode.StretchImage,
                Image = Program.LoadAppBitmap(appIcon)
            };
            var headerTitle = new Label
            {
                Location = new Point(68, 12),
                Size = new Size(370, 22),
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Text = "Uninstall Vault Print Tool v3.0"
            };
            var headerSubtitle = new Label
            {
                Location = new Point(68, 36),
                Size = new Size(370, 18),
                ForeColor = Color.DimGray,
                Text = "Autodesk Vault Professional Utility"
            };
            var divider = new Label { Dock = DockStyle.Bottom, Height = 1, BackColor = Color.LightGray };
            headerPanel.Controls.AddRange(new Control[] { headerIcon, headerTitle, headerSubtitle, divider });

            _statusLabel = new Label
            {
                Location = new Point(20, 80),
                Size = new Size(420, 36),
                Text = "Are you sure you want to completely remove Vault Print Tool v3.0 and all of its components?"
            };
            _progressBar = new ProgressBar
            {
                Location = new Point(20, 124),
                Size = new Size(420, 22),
                Minimum = 0,
                Maximum = 100,
                Visible = false
            };

            _uninstallButton = new Button
            {
                Location = new Point(272, 174),
                Size = new Size(84, 26),
                Text = "&Uninstall"
            };
            _uninstallButton.Click += OnUninstallClick;

            _cancelButton = new Button
            {
                Location = new Point(364, 174),
                Size = new Size(84, 26),
                Text = "Cancel"
            };
            _cancelButton.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { headerPanel, _statusLabel, _progressBar, _uninstallButton, _cancelButton });
            AcceptButton = _uninstallButton;
            CancelButton = _cancelButton;
        }

        private void OnUninstallClick(object sender, EventArgs e)
        {
            if (_completed)
            {
                Close();
                return;
            }

            _uninstallButton.Enabled = false;
            _cancelButton.Enabled = false;
            _progressBar.Visible = true;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Program.PerformUninstall((pct, msg) =>
                    {
                        if (!IsDisposed)
                        {
                            BeginInvoke((Action)(() =>
                            {
                                _progressBar.Value = Math.Max(0, Math.Min(100, pct));
                                _statusLabel.Text = msg;
                            }));
                        }
                    });

                    if (!IsDisposed)
                    {
                        BeginInvoke((Action)(() =>
                        {
                            _completed = true;
                            _statusLabel.Text = "Vault Print Tool v3.0 has been uninstalled successfully.";
                            _uninstallButton.Text = "Close";
                            _uninstallButton.Enabled = true;
                            _cancelButton.Visible = false;
                        }));
                    }
                }
                catch (Exception ex)
                {
                    if (!IsDisposed)
                    {
                        BeginInvoke((Action)(() =>
                        {
                            MessageBox.Show(this, "Uninstall failed:\r\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                            _uninstallButton.Enabled = true;
                            _cancelButton.Enabled = true;
                        }));
                    }
                }
            });
        }
    }
}
