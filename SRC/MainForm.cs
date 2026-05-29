using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DimSim_Windows_Repair
{
    public class MainForm : Form
    {
        // UI Controls
        private ComboBox cmbDrives;
        private Button btnCheckOS;
        private Button btnExtractProductKey;
        private Button btnManageStartupApps;
        private Label lblOSVersion, lblOSEdition, lblOSBuild, lblOSArch;
        private TextBox txtIsoPath;
        private Button btnBrowseISO;
        private Button btnScanImage;
        private ComboBox cmbEditions;
        private CheckBox chkAdvanced;
        private Panel pnlAdvanced;
        private CheckBox chkBypassArch;
        private CheckBox chkBypassBuild;
        private CheckBox chkUseLocalStore;
        private CheckBox chkUseWSUS;
        private Button btnRepair;
        private Button btnRevertUpdates;
        private Button btnManageUpdates;
        private Button btnBCDManagement;
        private RichTextBox rtbOutput;

        // Data storage
        private string offlineDrive = null;
        private string offlineEdition = null;
        private string offlineBuild = null;
        private string offlineArch = null;
        private string offlineVersionDisplay = "-";

        // Cancellation support for repair
        private CancellationTokenSource _cts = null;
        private Process _currentProcess = null;
        private object _processLock = new object();

        public MainForm()
        {
            InitializeComponent();
            SetupEventHandlers();
        }

        private void InitializeComponent()
        {
            this.Text = "DimSim Windows Repair";
            this.Size = new System.Drawing.Size(800, 920);
            this.MinimumSize = new System.Drawing.Size(800, 920);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = System.Drawing.Color.WhiteSmoke;

            TableLayoutPanel mainTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 11,
                Padding = new Padding(10, 30, 10, 10), // Top padding for menu bar
                AutoSize = true
            };

            mainTable.ColumnStyles.Clear();
            mainTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            mainTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F)); // 0: Drive selection
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F)); // 1: OS Info
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F)); // 2: Revert + Manage Updates
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 10F)); // 3: Separator line
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F)); // 4: Extract Product Key + Manage Startup Apps
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F)); // 5: BCD Management
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F)); // 6: WIM/ESD path
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F)); // 7: Scan Image button
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F)); // 8: Editions dropdown
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F)); // 9: Advanced options
            mainTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // 10: Repair button + console

            // Row 0: Drive selection
            Label lblDrive = new Label
            {
                Text = "Target OS Drive:",
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                Font = new Font(this.Font, FontStyle.Bold)
            };
            cmbDrives = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            btnCheckOS = new Button { Text = "Check OS", Dock = DockStyle.Fill, Width = 90, Anchor = AnchorStyles.Left };
            TableLayoutPanel drivePanel = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill };
            drivePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70F));
            drivePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            drivePanel.Controls.Add(cmbDrives, 0, 0);
            drivePanel.Controls.Add(btnCheckOS, 1, 0);
            mainTable.Controls.Add(lblDrive, 0, 0);
            mainTable.Controls.Add(drivePanel, 1, 0);

            // Row 1: OS Info
            Panel osInfoContainer = new Panel { Dock = DockStyle.Fill, AutoSize = true, Height = 35 };
            Label lblOSInfo = new Label
            {
                Text = "OS Details:",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(this.Font, FontStyle.Bold),
                Location = new Point(0, 5),
                AutoSize = true
            };
            FlowLayoutPanel osInfoPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                Margin = new Padding(0, 10, 0, 0),
                Location = new Point(lblOSInfo.Right + 5, 0)
            };
            lblOSVersion = new Label { Text = "Version: -", TextAlign = ContentAlignment.MiddleLeft, AutoSize = true, Padding = new Padding(68, 5, 5, 0) };
            lblOSEdition = new Label { Text = "Edition: -", TextAlign = ContentAlignment.MiddleLeft, AutoSize = true, Padding = new Padding(0, 5, 5, 0) };
            lblOSBuild = new Label { Text = "Build: -", TextAlign = ContentAlignment.MiddleLeft, AutoSize = true, Padding = new Padding(0, 5, 5, 0) };
            lblOSArch = new Label { Text = "Arch: -", TextAlign = ContentAlignment.MiddleLeft, AutoSize = true, Padding = new Padding(0, 5, 5, 0) };
            osInfoPanel.Controls.AddRange(new Control[] { lblOSVersion, lblOSEdition, lblOSBuild, lblOSArch });
            osInfoPanel.Location = new Point(lblOSInfo.Right + 5, (osInfoContainer.Height - osInfoPanel.Height) / 2);
            osInfoContainer.Controls.Add(lblOSInfo);
            osInfoContainer.Controls.Add(osInfoPanel);
            mainTable.Controls.Add(osInfoContainer, 0, 1);
            mainTable.SetColumnSpan(osInfoContainer, 2);

            // Row 2: Revert Pending Updates and Manage Updates
            btnRevertUpdates = new Button
            {
                Text = "Revert Pending Updates (MAY Fix Boot Loop)",
                Enabled = false,
                Dock = DockStyle.Fill,
                Height = 35,
                BackColor = Color.Orange,
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat
            };
            btnManageUpdates = new Button
            {
                Text = "Manage Updates",
                Enabled = false,
                Dock = DockStyle.Fill,
                Height = 35,
                BackColor = Color.LightBlue,
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat
            };
            mainTable.Controls.Add(btnRevertUpdates, 0, 2);
            mainTable.Controls.Add(btnManageUpdates, 1, 2);

            // Row 3: Separator
            Panel separator = new Panel
            {
                Dock = DockStyle.Fill,
                Height = 1,
                BackColor = Color.Gray,
                BorderStyle = BorderStyle.None,
                Margin = new Padding(0, 4, 0, 4)
            };
            mainTable.Controls.Add(separator, 0, 3);
            mainTable.SetColumnSpan(separator, 2);

            // Row 4: Extract Product Key and Manage Startup Apps
            btnExtractProductKey = new Button
            {
                Text = "Extract Product Key",
                Enabled = false,
                Dock = DockStyle.Fill,
                Height = 35,
                BackColor = Color.LightGreen,
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat
            };
            btnManageStartupApps = new Button
            {
                Text = "Manage Startup Apps",
                Enabled = false,
                Dock = DockStyle.Fill,
                Height = 35,
                BackColor = Color.LightGoldenrodYellow,
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat
            };
            mainTable.Controls.Add(btnExtractProductKey, 0, 4);
            mainTable.Controls.Add(btnManageStartupApps, 1, 4);

            // Row 5: BCD Management button
            btnBCDManagement = new Button
            {
                Text = "BCD Management",
                Enabled = false,
                Dock = DockStyle.Fill,
                Height = 35,
                BackColor = Color.LightGray,
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat
            };
            mainTable.Controls.Add(btnBCDManagement, 0, 5);
            mainTable.SetColumnSpan(btnBCDManagement, 2);

            // Row 6: WIM/ESD Selection Area
            Label lblIso = new Label
            {
                Text = "WIM/ESD:",
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                Font = new Font(this.Font, FontStyle.Bold)
            };
            txtIsoPath = new TextBox { Dock = DockStyle.Fill };
            btnBrowseISO = new Button { Text = "Browse", Dock = DockStyle.Fill, Width = 70, Anchor = AnchorStyles.Right };
            TableLayoutPanel isoPanel = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill };
            isoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 80F));
            isoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            isoPanel.Controls.Add(txtIsoPath, 0, 0);
            isoPanel.Controls.Add(btnBrowseISO, 1, 0);
            mainTable.Controls.Add(lblIso, 0, 6);
            mainTable.Controls.Add(isoPanel, 1, 6);

            // Row 7: Scan Image button
            btnScanImage = new Button
            {
                Text = "Scan Image",
                Dock = DockStyle.Fill,
                Width = 100,
                Anchor = AnchorStyles.Left
            };
            mainTable.Controls.Add(new Label(), 0, 7);
            mainTable.Controls.Add(btnScanImage, 1, 7);

            // Row 8: Editions dropdown
            Label lblEditions = new Label
            {
                Text = "Available Editions:",
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill
            };
            cmbEditions = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false, Dock = DockStyle.Fill };
            mainTable.Controls.Add(lblEditions, 0, 8);
            mainTable.Controls.Add(cmbEditions, 1, 8);

            // Row 9: Advanced Options
            chkAdvanced = new CheckBox
            {
                Text = "Show Advanced Bypass Options",
                Dock = DockStyle.Fill,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlAdvanced = new Panel { Dock = DockStyle.Fill, Height = 100, Visible = false };
            chkBypassArch = new CheckBox { Text = "Bypass Architecture (x86/x64) Check", AutoSize = true, Location = new Point(5, 5) };
            chkBypassBuild = new CheckBox { Text = "Bypass Build Number Check", AutoSize = true, Location = new Point(5, 30) };
            chkUseLocalStore = new CheckBox
            {
                Text = "Use local component store (no external WIM/ESD required)",
                AutoSize = true,
                Location = new Point(5, 55)
            };
            chkUseWSUS = new CheckBox
            {
                Text = "Attempt to use WSUS / Windows Update (remove /LimitAccess)",
                AutoSize = true,
                Location = new Point(5, 80),
                Enabled = false
            };
            pnlAdvanced.Controls.Add(chkBypassArch);
            pnlAdvanced.Controls.Add(chkBypassBuild);
            pnlAdvanced.Controls.Add(chkUseLocalStore);
            pnlAdvanced.Controls.Add(chkUseWSUS);
            mainTable.Controls.Add(chkAdvanced, 0, 9);
            mainTable.Controls.Add(pnlAdvanced, 1, 9);

            // Row 10: Repair button and console
            btnRepair = new Button
            {
                Text = "Repair",
                Enabled = false,
                Dock = DockStyle.Fill,
                Height = 40,
                BackColor = Color.SteelBlue,
                ForeColor = Color.White
            };
            rtbOutput = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.Black,
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 9)
            };
            mainTable.Controls.Add(btnRepair, 0, 10);
            mainTable.SetColumnSpan(btnRepair, 2);
            mainTable.Controls.Add(rtbOutput, 0, 10);
            mainTable.SetColumnSpan(rtbOutput, 2);
            mainTable.SetRowSpan(rtbOutput, 1);

            this.Controls.Add(mainTable);

            // Menu bar
            MenuStrip mainMenu = new MenuStrip();
            ToolStripMenuItem aboutMenuItem = new ToolStripMenuItem("About");
            aboutMenuItem.Click += (s, e) => ShowAboutDialog();
            mainMenu.Items.Add(aboutMenuItem);
            this.MainMenuStrip = mainMenu;
            this.Controls.Add(mainMenu);

            PopulateDriveList();
            chkUseWSUS.Enabled = false;
            btnRepair.Enabled = false;
        }

        private void ShowAboutDialog()
        {
            Form aboutForm = new Form();
            aboutForm.Text = "About DimSim Windows Repair";
            aboutForm.Size = new Size(550, 280);
            aboutForm.StartPosition = FormStartPosition.CenterParent;
            aboutForm.FormBorderStyle = FormBorderStyle.FixedDialog;
            aboutForm.MaximizeBox = false;
            aboutForm.MinimizeBox = false;

            Label lblText = new Label
            {
                Text = "DimSim Windows Repair is a graphical tool for repairing offline Windows installations. It allows you to perform system health checks, restore corrupted system files, manage startup entries, edit the Boot Configuration Data (BCD), install or uninstall Windows updates, and add Features on Demand (FoD) – all without booting into the target operating system.\n\nMade by Liams Electronics Lab",
                AutoSize = false,
                Size = new Size(500, 120),
                Location = new Point(20, 20),
                TextAlign = ContentAlignment.TopLeft
            };

            LinkLabel linkGithub = new LinkLabel
            {
                Text = "GitHub: https://github.com/Liams-Electronics-Lab",
                Location = new Point(20, 160),
                AutoSize = true
            };
            linkGithub.LinkClicked += (s, e) => Process.Start(new ProcessStartInfo("https://github.com/Liams-Electronics-Lab") { UseShellExecute = true });

            LinkLabel linkYoutube = new LinkLabel
            {
                Text = "YouTube: https://www.youtube.com/@Slot1Gamer",
                Location = new Point(20, 190),
                AutoSize = true
            };
            linkYoutube.LinkClicked += (s, e) => Process.Start(new ProcessStartInfo("https://www.youtube.com/@Slot1Gamer") { UseShellExecute = true });

            Button btnOk = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(220, 220),
                Size = new Size(80, 30)
            };

            aboutForm.Controls.Add(lblText);
            aboutForm.Controls.Add(linkGithub);
            aboutForm.Controls.Add(linkYoutube);
            aboutForm.Controls.Add(btnOk);
            aboutForm.AcceptButton = btnOk;

            aboutForm.ShowDialog(this);
        }

        private void SetupEventHandlers()
        {
            btnCheckOS.Click += BtnCheckOS_Click;
            btnExtractProductKey.Click += BtnExtractProductKey_Click;
            btnManageStartupApps.Click += BtnManageStartupApps_Click;
            btnBrowseISO.Click += BtnBrowseISO_Click;
            btnScanImage.Click += BtnScanImage_Click;
            btnRepair.Click += BtnRepair_Click;
            btnRevertUpdates.Click += BtnRevertUpdates_Click;
            btnManageUpdates.Click += BtnManageUpdates_Click;
            btnBCDManagement.Click += BtnBCDManagement_Click;
            chkAdvanced.CheckedChanged += (s, e) => pnlAdvanced.Visible = chkAdvanced.Checked;
            chkUseLocalStore.CheckedChanged += (s, e) =>
            {
                bool localMode = chkUseLocalStore.Checked;
                chkUseWSUS.Enabled = localMode;
                txtIsoPath.Enabled = !localMode;
                btnBrowseISO.Enabled = !localMode;
                btnScanImage.Enabled = !localMode;
                cmbEditions.Enabled = !localMode && cmbEditions.Items.Count > 0;
                btnRepair.Enabled = !string.IsNullOrEmpty(offlineDrive) && (localMode || cmbEditions.Items.Count > 0);
            };
            this.FormClosing += (s, e) => CleanupRegistryHive();
        }

        private void PopulateDriveList()
        {
            cmbDrives.Items.Clear();
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                {
                    string root = drive.RootDirectory.FullName.TrimEnd('\\');
                    if (!root.Equals("C:", StringComparison.OrdinalIgnoreCase))
                    {
                        cmbDrives.Items.Add(root);
                    }
                }
            }
            if (cmbDrives.Items.Count > 0)
                cmbDrives.SelectedIndex = 0;
        }

        private async void BtnCheckOS_Click(object sender, EventArgs e)
        {
            if (cmbDrives.SelectedItem == null)
            {
                MessageBox.Show("Please select a target drive.", "No Drive", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            offlineDrive = cmbDrives.SelectedItem.ToString();
            try
            {
                await Task.Run(() => ReadOfflineOSInfo(offlineDrive));
                UpdateOSInfoLabels();
                btnExtractProductKey.Enabled = true;
                btnManageStartupApps.Enabled = true;
                btnRevertUpdates.Enabled = true;
                btnManageUpdates.Enabled = true;
                btnBCDManagement.Enabled = true;
                btnRepair.Enabled = chkUseLocalStore.Checked || cmbEditions.Items.Count > 0;
                MessageBox.Show("OS information retrieved successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to read OS information: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                offlineEdition = offlineBuild = offlineArch = null;
                offlineVersionDisplay = "-";
                UpdateOSInfoLabels();
                btnExtractProductKey.Enabled = false;
                btnManageStartupApps.Enabled = false;
                btnRevertUpdates.Enabled = false;
                btnManageUpdates.Enabled = false;
                btnBCDManagement.Enabled = false;
                btnRepair.Enabled = false;
            }
        }

        private void ReadOfflineOSInfo(string driveRoot)
        {
            string windowsPath = Path.Combine(driveRoot, "Windows");
            if (!Directory.Exists(windowsPath))
                throw new Exception("Windows directory not found on selected drive.");

            bool success = false;

            // Attempt 1: Registry method (requires admin)
            try
            {
                string softwareHive = Path.Combine(windowsPath, "System32", "config", "SOFTWARE");
                if (!File.Exists(softwareHive))
                    throw new Exception("SOFTWARE hive missing.");

                using (Process regLoad = new Process())
                {
                    regLoad.StartInfo.FileName = "reg.exe";
                    regLoad.StartInfo.Arguments = $"load HKLM\\Offline_Software \"{softwareHive}\"";
                    regLoad.StartInfo.CreateNoWindow = true;
                    regLoad.StartInfo.UseShellExecute = false;
                    regLoad.Start();
                    regLoad.WaitForExit();
                    if (regLoad.ExitCode != 0)
                        throw new Exception("Failed to load registry hive (admin required).");
                }

                try
                {
                    using (var offlineKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey("Offline_Software\\Microsoft\\Windows NT\\CurrentVersion"))
                    {
                        if (offlineKey == null)
                            throw new Exception("Cannot open CurrentVersion key.");

                        offlineEdition = offlineKey.GetValue("EditionID") as string ?? "Unknown";
                        string rawProductName = offlineKey.GetValue("ProductName") as string ?? "Unknown";
                        offlineBuild = offlineKey.GetValue("CurrentBuild") as string ?? "0";

                        if (int.TryParse(offlineBuild, out int buildNum) && buildNum >= 22000)
                        {
                            if (rawProductName.Contains("Windows 10"))
                                rawProductName = rawProductName.Replace("Windows 10", "Windows 11");
                        }
                        offlineVersionDisplay = rawProductName;
                    }

                    offlineArch = Directory.Exists(Path.Combine(windowsPath, "SysWOW64")) ? "x64" : "x86";
                    success = true;
                    AppendToOutput("[Registry] OS info retrieved successfully.\r\n");
                }
                finally
                {
                    using (Process regUnload = new Process())
                    {
                        regUnload.StartInfo.FileName = "reg.exe";
                        regUnload.StartInfo.Arguments = "unload HKLM\\Offline_Software";
                        regUnload.StartInfo.CreateNoWindow = true;
                        regUnload.StartInfo.UseShellExecute = false;
                        regUnload.Start();
                        regUnload.WaitForExit(1000);
                    }
                }
            }
            catch (Exception ex)
            {
                AppendToOutput($"[Registry] Failed: {ex.Message}. Falling back to file version...\r\n");
            }

            // Attempt 2: File version fallback (no admin required)
            if (!success)
            {
                try
                {
                    string ntosPath = Path.Combine(windowsPath, "System32", "ntoskrnl.exe");
                    if (!File.Exists(ntosPath))
                        throw new Exception("ntoskrnl.exe not found.");

                    var fileVer = FileVersionInfo.GetVersionInfo(ntosPath);
                    string[] parts = fileVer.FileVersion.Split('.');
                    string build = (parts.Length >= 3) ? parts[2] : (parts.Length >= 2 ? parts[1] : "0");
                    int buildNum = int.Parse(build);
                    string friendlyName;
                    if (buildNum >= 22000)
                        friendlyName = "Windows 11";
                    else if (buildNum >= 10240)
                        friendlyName = "Windows 10";
                    else if (buildNum >= 9600)
                        friendlyName = "Windows 8.1";
                    else if (buildNum >= 9200)
                        friendlyName = "Windows 8";
                    else if (buildNum >= 7600)
                        friendlyName = "Windows 7";
                    else
                        friendlyName = $"Windows (build {build})";

                    offlineBuild = build;
                    offlineVersionDisplay = friendlyName;
                    offlineEdition = "Unknown (fallback)";
                    offlineArch = Directory.Exists(Path.Combine(windowsPath, "SysWOW64")) ? "x64" : "x86";
                    success = true;
                    AppendToOutput($"[Fallback] Detected: {friendlyName} Build {build} {offlineArch}\r\n");
                }
                catch (Exception ex)
                {
                    AppendToOutput($"[Fallback] ERROR: {ex.Message}\r\n");
                    throw new Exception("Both registry and fallback methods failed to read OS info.");
                }
            }

            if (!success)
                throw new Exception("Unable to determine OS information.");

            if (string.IsNullOrEmpty(offlineVersionDisplay)) offlineVersionDisplay = "Unknown";
            if (string.IsNullOrEmpty(offlineBuild)) offlineBuild = "0";
            if (string.IsNullOrEmpty(offlineArch)) offlineArch = "Unknown";
            if (string.IsNullOrEmpty(offlineEdition)) offlineEdition = "Unknown";
        }

        private void UpdateOSInfoLabels()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(UpdateOSInfoLabels));
                return;
            }
            lblOSVersion.Text = $"Version: {offlineVersionDisplay}";
            lblOSEdition.Text = $"Edition: {offlineEdition ?? "-"}";
            lblOSBuild.Text = $"Build: {offlineBuild ?? "-"}";
            lblOSArch.Text = $"Arch: {offlineArch ?? "-"}";
        }

        // ========== PRODUCT KEY EXTRACTION ==========
        private async void BtnExtractProductKey_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(offlineDrive))
            {
                MessageBox.Show("Please click 'Check OS' first.", "No OS Info", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnExtractProductKey.Enabled = false;
            try
            {
                await Task.Run(() => ExtractProductKeyToConsole(offlineDrive));
            }
            catch (Exception ex)
            {
                AppendToOutput($"ERROR: {ex.Message}\r\n");
            }
            finally
            {
                btnExtractProductKey.Enabled = true;
            }
        }

        private void ExtractProductKeyToConsole(string driveRoot)
        {
            string windowsPath = Path.Combine(driveRoot, "Windows");
            string softwareHive = Path.Combine(windowsPath, "System32", "config", "SOFTWARE");

            AppendToOutput($"Loading offline registry hive from {softwareHive}...\r\n");

            if (!File.Exists(softwareHive))
            {
                AppendToOutput("ERROR: SOFTWARE hive not found. Is this a valid Windows installation?\r\n");
                return;
            }

            using (Process regLoad = new Process())
            {
                regLoad.StartInfo.FileName = "reg.exe";
                regLoad.StartInfo.Arguments = $"load HKLM\\Offline_Software \"{softwareHive}\"";
                regLoad.StartInfo.CreateNoWindow = true;
                regLoad.StartInfo.UseShellExecute = false;
                regLoad.Start();
                regLoad.WaitForExit();
                if (regLoad.ExitCode != 0)
                {
                    AppendToOutput("ERROR: Failed to load registry hive. Ensure you have administrative privileges.\r\n");
                    return;
                }
            }

            try
            {
                using (Microsoft.Win32.RegistryKey cvKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey("Offline_Software\\Microsoft\\Windows NT\\CurrentVersion"))
                {
                    if (cvKey == null)
                    {
                        AppendToOutput("ERROR: Cannot open CurrentVersion registry key.\r\n");
                        return;
                    }

                    AppendToOutput("\r\n=== PRODUCT KEY SCAN RESULTS ===\r\n");

                    byte[] dpi = cvKey.GetValue("DigitalProductId") as byte[];
                    string legacyKey = null;
                    if (dpi != null && dpi.Length >= 67)
                        legacyKey = DecodeProductKey(dpi);
                    if (!string.IsNullOrEmpty(legacyKey))
                        AppendToOutput($"DigitalProductId (Legacy Binary): {legacyKey}\r\n");

                    string backupKey = null;
                    using (Microsoft.Win32.RegistryKey sppKey = cvKey.OpenSubKey("SoftwareProtectionPlatform"))
                    {
                        if (sppKey != null)
                            backupKey = sppKey.GetValue("BackupProductKeyDefault") as string;
                    }
                    if (!string.IsNullOrEmpty(backupKey))
                        AppendToOutput($"BackupProductKeyDefault (Plain Text): {backupKey}\r\n");

                    byte[] dpi4 = cvKey.GetValue("DigitalProductId4") as byte[];
                    string modernKey = null;
                    if (dpi4 != null && dpi4.Length >= 67)
                        modernKey = DecodeProductKey(dpi4);
                    if (!string.IsNullOrEmpty(modernKey))
                        AppendToOutput($"DigitalProductId4 (Modern Binary): {modernKey}\r\n");

                    if (legacyKey == null && backupKey == null && modernKey == null)
                    {
                        AppendToOutput("No product keys found in standard registry locations.\r\n");
                        AppendToOutput("This installation may use KMS (Volume Licensing) or AVMA (Hyper-V pass-through).\r\n");
                    }

                    AppendToOutput("================================\r\n");
                }
            }
            finally
            {
                using (Process regUnload = new Process())
                {
                    regUnload.StartInfo.FileName = "reg.exe";
                    regUnload.StartInfo.Arguments = "unload HKLM\\Offline_Software";
                    regUnload.StartInfo.CreateNoWindow = true;
                    regUnload.StartInfo.UseShellExecute = false;
                    regUnload.Start();
                    regUnload.WaitForExit();
                }
                AppendToOutput("\r\nSafely unloading registry hive...\r\nDone.\r\n");
            }
        }

        private string DecodeProductKey(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 67)
                return null;

            int keyOffset = 52;
            bool isWin8 = (bytes[66] >> 3) == 1;
            bytes[66] = (byte)((bytes[66] & 0xF7) | ((isWin8 ? 2 : 0) * 4));

            string chars = "BCDFGHJKMPQRTVWXY2346789";
            byte[] hexKey = new byte[15];
            Array.Copy(bytes, keyOffset, hexKey, 0, 15);

            StringBuilder decoded = new StringBuilder();
            int last = 0;
            for (int i = 24; i >= 0; i--)
            {
                int current = 0;
                for (int j = 14; j >= 0; j--)
                {
                    current = (current * 256) ^ hexKey[j];
                    hexKey[j] = (byte)(current / 24);
                    current = current % 24;
                }
                decoded.Insert(0, chars[current]);
                last = current;
            }

            if (isWin8)
            {
                string key = decoded.ToString();
                if (last >= 0 && last < key.Length && last > 0)
                {
                    string part1 = key.Substring(1, last);
                    string part2 = key.Substring(last + 1);
                    decoded = new StringBuilder(part1 + "N" + part2);
                }
            }

            string rawKey = decoded.ToString();
            if (rawKey.Length != 25)
                return "Invalid key length";

            string formatted = "";
            for (int i = 0; i < 25; i++)
            {
                formatted += rawKey[i];
                if ((i + 1) % 5 == 0 && i != 24)
                    formatted += "-";
            }

            if (formatted == "BBBBB-BBBBB-BBBBB-BBBBB-BBBBB")
                return "Blank (All zeroes)";

            return formatted;
        }

        // ========== MANAGE STARTUP APPS ==========
        private void BtnManageStartupApps_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(offlineDrive))
            {
                MessageBox.Show("Please click 'Check OS' first.", "No OS Info", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var startupForm = new StartupAppsForm(offlineDrive);
            startupForm.ShowDialog(this);
        }

        // ========== BCD MANAGEMENT ==========
        private void BtnBCDManagement_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(offlineDrive))
            {
                MessageBox.Show("Please click 'Check OS' first.", "No OS Info", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var bcdForm = new BCDManagementForm(offlineDrive);
            bcdForm.ShowDialog(this);
        }

        // ========== IMAGE SCANNING ==========
        private void BtnBrowseISO_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "Select Windows installation image (WIM/ESD)";
                ofd.Filter = "Windows Image Files|*.wim;*.esd|All Files|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    txtIsoPath.Text = ofd.FileName;
                }
            }
        }

        private async void BtnScanImage_Click(object sender, EventArgs e)
        {
            string imagePath = txtIsoPath.Text.Trim();
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                MessageBox.Show("Please select a valid WIM or ESD file.", "File Not Found", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            btnScanImage.Enabled = false;
            rtbOutput.AppendText($"[Scanning] {imagePath} - retrieving edition list...\r\n");
            try
            {
                List<ImageEdition> editions = await Task.Run(() => GetEditionsFromImage(imagePath));
                if (editions.Count == 0)
                {
                    MessageBox.Show("No editions found in the image.", "Scan Result", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                cmbEditions.Items.Clear();
                cmbEditions.DisplayMember = "DisplayName";
                cmbEditions.ValueMember = "Index";
                foreach (var ed in editions)
                    cmbEditions.Items.Add(ed);
                cmbEditions.Enabled = true;
                cmbEditions.SelectedIndex = -1;

                if (!string.IsNullOrEmpty(offlineEdition))
                {
                    int bestMatchIndex = FindBestMatchingEdition(editions, offlineEdition);
                    if (bestMatchIndex >= 0)
                        cmbEditions.SelectedIndex = bestMatchIndex;
                }

                btnRepair.Enabled = !string.IsNullOrEmpty(offlineDrive) && (chkUseLocalStore.Checked || cmbEditions.Items.Count > 0);
                rtbOutput.AppendText($"Scan complete. Found {editions.Count} edition(s).\r\n");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error scanning image: {ex.Message}", "Scan Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                rtbOutput.AppendText($"ERROR: {ex.Message}\r\n");
            }
            finally
            {
                btnScanImage.Enabled = true;
            }
        }

        private class ImageEdition
        {
            public int Index { get; set; }
            public string Name { get; set; }
            public string DisplayName => $"{Index}: {Name}";
        }

        private List<ImageEdition> GetEditionsFromImage(string imagePath)
        {
            List<ImageEdition> editions = new List<ImageEdition>();
            string output = RunDISMCommand($"/Get-WimInfo /WimFile:\"{imagePath}\"");
            Regex regex = new Regex(@"Index\s*:\s*(\d+)\s*Name\s*:\s*(.+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            var matches = regex.Matches(output);
            foreach (Match match in matches)
            {
                if (match.Groups.Count == 3)
                {
                    int idx = int.Parse(match.Groups[1].Value);
                    string name = match.Groups[2].Value.Trim();
                    editions.Add(new ImageEdition { Index = idx, Name = name });
                }
            }
            return editions;
        }

        private int FindBestMatchingEdition(List<ImageEdition> editions, string targetEditionId)
        {
            for (int i = 0; i < editions.Count; i++)
            {
                string name = editions[i].Name.ToLowerInvariant();
                string target = targetEditionId.ToLowerInvariant();
                if (name.Contains(target) || target.Contains(name))
                    return i;
            }
            return -1;
        }

        // ========== REPAIR LOGIC ==========
        private async void BtnRepair_Click(object sender, EventArgs e)
        {
            if (_cts != null)
            {
                await RequestCancellation();
                return;
            }

            if (string.IsNullOrEmpty(offlineDrive))
            {
                MessageBox.Show("Please click 'Check OS' first.", "No OS Info", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool localMode = chkUseLocalStore.Checked;
            bool useWSUS = chkUseWSUS.Checked && localMode;

            // If not local mode, require a valid WIM/ESD and edition
            if (!localMode)
            {
                string imagePath = txtIsoPath.Text.Trim();
                if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                {
                    MessageBox.Show("Please select a valid WIM or ESD file.", "File Not Found", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (cmbEditions.SelectedItem == null)
                {
                    MessageBox.Show("Please select an edition from the ISO.", "No Edition", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            SetControlsEnabledDuringOperation(false);
            btnRepair.Text = "STOP";
            btnRepair.BackColor = Color.Red;
            btnRepair.ForeColor = Color.White;
            _cts = new CancellationTokenSource();

            try
            {
                rtbOutput.Clear();

                if (localMode)
                {
                    rtbOutput.AppendText($"=== Starting local repair for {offlineDrive} using local component store ===\r\n");
                    await RunCommandAndLog($"dism.exe /Image:{offlineDrive}\\ /Cleanup-Image /CheckHealth", "CheckHealth", _cts.Token);

                    string restoreCmd = useWSUS
                        ? $"dism.exe /Image:{offlineDrive}\\ /Cleanup-Image /RestoreHealth"
                        : $"dism.exe /Image:{offlineDrive}\\ /Cleanup-Image /RestoreHealth /LimitAccess";
                    await RunCommandAndLog(restoreCmd, "RestoreHealth (local)", _cts.Token);

                    await RunCommandAndLog($"sfc.exe /scannow /offbootdir={offlineDrive}\\ /offwindir={offlineDrive}\\Windows", "SFC Scan", _cts.Token);
                }
                else
                {
                    string imagePath = txtIsoPath.Text.Trim();
                    ImageEdition selectedEdition = (ImageEdition)cmbEditions.SelectedItem;
                    int index = selectedEdition.Index;

                    if (!chkBypassArch.Checked)
                    {
                        string imageArch = await Task.Run(() => GetImageArchitecture(imagePath, index));
                        if (!string.Equals(imageArch, offlineArch, StringComparison.OrdinalIgnoreCase))
                        {
                            MessageBox.Show($"Architecture mismatch: Offline OS is {offlineArch}, ISO edition is {imageArch}. Use bypass option to ignore.", "Validation Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                    }

                    if (!chkBypassBuild.Checked)
                    {
                        string imageBuild = await Task.Run(() => GetImageBuildNumber(imagePath, index));
                        if (!string.Equals(imageBuild, offlineBuild, StringComparison.OrdinalIgnoreCase))
                        {
                            var result = MessageBox.Show($"Build number mismatch: Offline OS build is {offlineBuild}, ISO edition build is {imageBuild}.\nDo you want to continue anyway? (Bypass checkbox will silence this)", "Build Mismatch", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                            if (result != DialogResult.Yes)
                                return;
                        }
                    }

                    rtbOutput.AppendText($"=== Starting repair for {offlineDrive} using external image ===\r\n");
                    rtbOutput.AppendText($"ISO: {imagePath}, Index: {index}\r\n\r\n");

                    await RunCommandAndLog($"dism.exe /Image:{offlineDrive}\\ /Cleanup-Image /CheckHealth", "CheckHealth", _cts.Token);
                    await RunCommandAndLog($"dism.exe /Image:{offlineDrive}\\ /Cleanup-Image /RestoreHealth /Source:WIM:\"{imagePath}\":{index} /LimitAccess", "RestoreHealth", _cts.Token);
                    await RunCommandAndLog($"sfc.exe /scannow /offbootdir={offlineDrive}\\ /offwindir={offlineDrive}\\Windows", "SFC Scan", _cts.Token);
                }

                if (!_cts.Token.IsCancellationRequested)
                {
                    rtbOutput.AppendText("\r\n=== Repair completed successfully ===\r\n");
                    MessageBox.Show("Repair process finished. Please check the console output for any errors.", "Repair Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    rtbOutput.AppendText("\r\n=== Repair cancelled by user ===\r\n");
                }
            }
            catch (OperationCanceledException)
            {
                rtbOutput.AppendText("\r\n=== Repair cancelled by user ===\r\n");
            }
            catch (Exception ex)
            {
                rtbOutput.AppendText($"\r\nFATAL ERROR: {ex.Message}\r\n");
                MessageBox.Show($"Repair failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                btnRepair.Text = "Repair";
                btnRepair.BackColor = Color.SteelBlue;
                btnRepair.ForeColor = Color.White;
                SetControlsEnabledDuringOperation(true);
                // Re-apply enable logic after operation
                btnRepair.Enabled = !string.IsNullOrEmpty(offlineDrive) && (chkUseLocalStore.Checked || cmbEditions.Items.Count > 0);
            }
        }

        private async Task RequestCancellation()
        {
            using (Form confirmDialog = new Form())
            {
                confirmDialog.Text = "Confirm Cancel";
                confirmDialog.Size = new Size(450, 200);
                confirmDialog.StartPosition = FormStartPosition.CenterParent;
                confirmDialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                confirmDialog.MaximizeBox = false;
                confirmDialog.MinimizeBox = false;

                Label warningLabel = new Label
                {
                    Text = "WARNING: Stopping the repair early may leave the offline Windows installation in an inconsistent or unbootable state.\n\nAre you sure you want to stop?",
                    AutoSize = false,
                    Size = new Size(400, 80),
                    Location = new Point(20, 20),
                    TextAlign = ContentAlignment.MiddleLeft
                };

                Button stopButton = new Button
                {
                    Text = "Stop Repair (wait 20s)",
                    Enabled = false,
                    Size = new Size(120, 35),
                    Location = new Point(20, 110),
                    BackColor = Color.Red,
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat
                };
                Button cancelButton = new Button
                {
                    Text = "Cancel",
                    Size = new Size(120, 35),
                    Location = new Point(160, 110),
                    FlatStyle = FlatStyle.Flat
                };

                confirmDialog.Controls.Add(warningLabel);
                confirmDialog.Controls.Add(stopButton);
                confirmDialog.Controls.Add(cancelButton);

                cancelButton.Click += (s, e) => confirmDialog.DialogResult = DialogResult.Cancel;
                stopButton.Click += (s, e) => confirmDialog.DialogResult = DialogResult.OK;

                System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 1000 };
                int remaining = 20;
                timer.Tick += (s, e) =>
                {
                    remaining--;
                    if (remaining <= 0)
                    {
                        timer.Stop();
                        stopButton.Text = "Stop Repair";
                        stopButton.Enabled = true;
                    }
                    else
                    {
                        stopButton.Text = $"Stop Repair ({remaining}s)";
                    }
                };
                timer.Start();

                DialogResult result = confirmDialog.ShowDialog(this);
                timer.Stop();

                if (result == DialogResult.OK)
                {
                    _cts?.Cancel();
                    lock (_processLock)
                    {
                        if (_currentProcess != null && !_currentProcess.HasExited)
                        {
                            try { _currentProcess.Kill(); } catch { }
                        }
                    }
                    AppendToOutput("\r\n[USER] Cancellation requested. Terminating current operation...\r\n");
                }
            }
        }

        private async Task RunCommandAndLog(string commandLine, string stepName, CancellationToken token)
        {
            string[] parts = commandLine.Split(new[] { ' ' }, 2);
            string fileName = parts[0];
            string args = parts.Length > 1 ? parts[1] : "";

            await Task.Run(() =>
            {
                using (Process process = new Process())
                {
                    process.StartInfo.FileName = fileName;
                    process.StartInfo.Arguments = args;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.EnableRaisingEvents = true;

                    lock (_processLock) { _currentProcess = process; }

                    AppendToOutput($"[{stepName}] Starting: {commandLine}\r\n");
                    process.Start();

                    using (var outputWaitHandle = new AutoResetEvent(false))
                    using (var errorWaitHandle = new AutoResetEvent(false))
                    {
                        process.OutputDataReceived += (s, e) =>
                        {
                            if (e.Data != null) AppendToOutput(e.Data + "\r\n");
                            else outputWaitHandle.Set();
                        };
                        process.ErrorDataReceived += (s, e) =>
                        {
                            if (e.Data != null) AppendToOutput("[ERROR] " + e.Data + "\r\n");
                            else errorWaitHandle.Set();
                        };

                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        while (!process.HasExited)
                        {
                            if (token.IsCancellationRequested)
                            {
                                try { process.Kill(); } catch { }
                                break;
                            }
                            Thread.Sleep(100);
                        }
                        process.WaitForExit();
                        outputWaitHandle.WaitOne(5000);
                        errorWaitHandle.WaitOne(5000);
                    }

                    if (token.IsCancellationRequested)
                        AppendToOutput($"[{stepName}] Cancelled by user.\r\n\r\n");
                    else
                        AppendToOutput($"[{stepName}] Completed with exit code {process.ExitCode}\r\n\r\n");
                }
            }, token);
        }

        // ========== REVERT PENDING UPDATES ==========
        private async void BtnRevertUpdates_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(offlineDrive))
            {
                MessageBox.Show("Please click 'Check OS' first.", "No OS Info", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetControlsEnabledDuringOperation(false);

            rtbOutput.AppendText($"\r\n=== Starting rollback of pending updates on {offlineDrive} ===\r\n");
            rtbOutput.AppendText($"Running: dism /Image:{offlineDrive}\\ /Cleanup-Image /RevertPendingActions\r\n");

            try
            {
                bool success = await RunDismRevertPendingActions(offlineDrive);
                if (success)
                {
                    rtbOutput.AppendText("\r\n*** UPDATE ROLLBACK COMPLETE. It is recommended to reboot the target machine now. ***\r\n");
                    MessageBox.Show("Pending updates have been reverted. The target machine should be rebooted.", "Rollback Finished", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    rtbOutput.AppendText("\r\n*** Rollback completed with errors. Check the output above. ***\r\n");
                }
            }
            catch (Exception ex)
            {
                rtbOutput.AppendText($"\r\nERROR: {ex.Message}\r\n");
                MessageBox.Show($"Rollback failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetControlsEnabledDuringOperation(true);
                if (cmbEditions.Items.Count == 0)
                    btnRepair.Enabled = false;
                btnRevertUpdates.Enabled = true;
                btnManageUpdates.Enabled = true;
            }
        }

        private Task<bool> RunDismRevertPendingActions(string driveRoot)
        {
            return Task.Run(() =>
            {
                string args = $"/Image:{driveRoot}\\ /Cleanup-Image /RevertPendingActions";
                using (Process process = new Process())
                {
                    process.StartInfo.FileName = "dism.exe";
                    process.StartInfo.Arguments = args;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.EnableRaisingEvents = true;

                    process.OutputDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            AppendToOutput(e.Data + "\r\n");
                    };
                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            AppendToOutput("[ERROR] " + e.Data + "\r\n");
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();

                    AppendToOutput($"\r\nDISM exited with code {process.ExitCode}\r\n");
                    return process.ExitCode == 0;
                }
            });
        }

        // ========== MANAGE UPDATES ==========
        private void BtnManageUpdates_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(offlineDrive))
            {
                MessageBox.Show("Please click 'Check OS' first.", "No OS Info", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var updateForm = new UpdateManagerForm(offlineDrive);
            updateForm.ShowDialog(this);
        }

        // ========== HELPER METHODS ==========
        private void SetControlsEnabledDuringOperation(bool enabled)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<bool>(SetControlsEnabledDuringOperation), enabled);
                return;
            }
            btnRevertUpdates.Enabled = enabled;
            btnManageUpdates.Enabled = enabled;
            btnManageStartupApps.Enabled = enabled && !string.IsNullOrEmpty(offlineDrive);
            btnRepair.Enabled = enabled && !string.IsNullOrEmpty(offlineDrive) && (chkUseLocalStore.Checked || cmbEditions.Items.Count > 0);
            btnCheckOS.Enabled = enabled;
            btnScanImage.Enabled = enabled && !chkUseLocalStore.Checked;
            btnExtractProductKey.Enabled = enabled && !string.IsNullOrEmpty(offlineDrive);
            btnBCDManagement.Enabled = enabled && !string.IsNullOrEmpty(offlineDrive);
            cmbDrives.Enabled = enabled;
            txtIsoPath.Enabled = enabled && !chkUseLocalStore.Checked;
            btnBrowseISO.Enabled = enabled && !chkUseLocalStore.Checked;
            cmbEditions.Enabled = enabled && !chkUseLocalStore.Checked && cmbEditions.Items.Count > 0;
            chkAdvanced.Enabled = enabled;
            chkUseLocalStore.Enabled = enabled;
            chkUseWSUS.Enabled = enabled && chkUseLocalStore.Checked;
        }

        private string GetImageArchitecture(string imagePath, int index)
        {
            string output = RunDISMCommand($"/Get-ImageInfo /WimFile:\"{imagePath}\" /Index:{index}");
            Match match = Regex.Match(output, @"Architecture\s*:\s*(\w+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string arch = match.Groups[1].Value.ToLowerInvariant();
                if (arch.Contains("64")) return "x64";
                if (arch.Contains("86")) return "x86";
                return arch;
            }
            throw new Exception("Could not determine architecture from ISO image.");
        }

        private string GetImageBuildNumber(string imagePath, int index)
        {
            string output = RunDISMCommand($"/Get-ImageInfo /WimFile:\"{imagePath}\" /Index:{index}");
            Match buildMatch = Regex.Match(output, @"Build\s*:\s*(\d+)", RegexOptions.IgnoreCase);
            if (buildMatch.Success)
                return buildMatch.Groups[1].Value;

            Match versionMatch = Regex.Match(output, @"Version\s*:\s*[0-9]+\.[0-9]+\.(\d+)", RegexOptions.IgnoreCase);
            if (versionMatch.Success)
                return versionMatch.Groups[1].Value;

            throw new Exception("Could not retrieve build number from ISO image.");
        }

        private string RunDISMCommand(string arguments)
        {
            using (Process p = new Process())
            {
                p.StartInfo.FileName = "dism.exe";
                p.StartInfo.Arguments = arguments;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.Start();
                string output = p.StandardOutput.ReadToEnd();
                string error = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0 && string.IsNullOrEmpty(output))
                    output = error;
                return output;
            }
        }

        private void AppendToOutput(string text)
        {
            if (rtbOutput.InvokeRequired)
            {
                rtbOutput.Invoke(new Action<string>(AppendToOutput), text);
                return;
            }
            rtbOutput.AppendText(text);
            rtbOutput.ScrollToCaret();
        }

        private void CleanupRegistryHive()
        {
            try
            {
                using (Process regUnload = new Process())
                {
                    regUnload.StartInfo.FileName = "reg.exe";
                    regUnload.StartInfo.Arguments = "unload HKLM\\Offline_Software";
                    regUnload.StartInfo.CreateNoWindow = true;
                    regUnload.StartInfo.UseShellExecute = false;
                    regUnload.Start();
                    regUnload.WaitForExit(500);
                }
            }
            catch { }
        }
    }
}