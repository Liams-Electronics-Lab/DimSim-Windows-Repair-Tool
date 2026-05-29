using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

#nullable disable

namespace DimSim_Windows_Repair
{
    public class UpdateManagerForm : Form
    {
        private readonly string _offlineDrive;
        private DataGridView dgvUpdates;
        private RichTextBox rtbConsole;
        private TextBox txtKbNumber;
        private Button btnManualInstall;
        private Button btnRefreshScan;
        private Button btnSuperDeepScan;
        private Button btnManualCab;
        private ProgressBar pbScanProgress;
        private Label lblScanStatus;
        private CheckBox chkShowAll;
        private TextBox txtFodIsoPath;
        private Button btnBrowseFodIso;
        private Button btnInstallFeaturesFromFod;
        private TableLayoutPanel mainLayout;
        private CancellationTokenSource _scanCts;

        // PackageInfo class defined early
        private class PackageInfo
        {
            public string PackageName { get; set; }
            public DateTime InstallDate { get; set; }
            public string ReleaseType { get; set; }
            public string KBNumber { get; set; } = "Unknown";
        }

        public UpdateManagerForm(string offlineDrive)
        {
            _offlineDrive = offlineDrive;
            InitializeComponent();
            this.Load += async (s, e) => await LoadInstalledUpdatesAsync(false);
        }

        private void InitializeComponent()
        {
            this.Text = "Offline Windows Update Manager - " + _offlineDrive;
            this.Size = new Size(1200, 750);
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimumSize = new Size(1000, 600);

            mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(5)
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));

            Panel topPanel = new Panel { Dock = DockStyle.Fill, Height = 160 };

            // KB manual install section
            Label lblKb = new Label
            {
                Text = "Knowledge Base (KB) Number:",
                Location = new Point(10, 12),
                Size = new Size(170, 23),
                TextAlign = ContentAlignment.MiddleRight
            };
            txtKbNumber = new TextBox
            {
                Location = new Point(185, 10),
                Size = new Size(150, 23)
            };
            btnManualInstall = new Button
            {
                Text = "Download and Install",
                Location = new Point(345, 8),
                Size = new Size(140, 28),
                BackColor = Color.Gold,
                FlatStyle = FlatStyle.Flat
            };
            btnManualInstall.Click += BtnManualInstall_Click;

            btnRefreshScan = new Button
            {
                Text = "Refresh List (Fast)",
                Location = new Point(500, 8),
                Size = new Size(120, 28),
                BackColor = Color.LightGray,
                FlatStyle = FlatStyle.Flat
            };
            btnRefreshScan.Click += async (s, e) => await LoadInstalledUpdatesAsync(false);

            btnSuperDeepScan = new Button
            {
                Text = "SuperDeepScan (Slow)",
                Location = new Point(630, 8),
                Size = new Size(150, 28),
                BackColor = Color.LightCoral,
                FlatStyle = FlatStyle.Flat
            };
            btnSuperDeepScan.Click += async (s, e) => await LoadInstalledUpdatesAsync(true);

            chkShowAll = new CheckBox
            {
                Text = "Show all packages (read-only)",
                Location = new Point(795, 12),
                Size = new Size(180, 23),
                Checked = false
            };
            chkShowAll.CheckedChanged += async (s, e) => await LoadInstalledUpdatesAsync(false);

            // Features on Demand Management group
            GroupBox gbFod = new GroupBox
            {
                Text = "Features on Demand Management",
                Location = new Point(8, 50),
                Size = new Size(1160, 100),
                FlatStyle = FlatStyle.Flat
            };

            Label lblFod = new Label
            {
                Text = "FoD ISO / WIM Path:",
                Location = new Point(10, 25),
                Size = new Size(110, 23),
                TextAlign = ContentAlignment.MiddleRight
            };
            txtFodIsoPath = new TextBox
            {
                Location = new Point(125, 23),
                Size = new Size(500, 23)
            };
            btnBrowseFodIso = new Button
            {
                Text = "Browse...",
                Location = new Point(635, 21),
                Size = new Size(80, 28),
                FlatStyle = FlatStyle.Flat
            };
            btnBrowseFodIso.Click += BtnBrowseFodIso_Click;

            btnInstallFeaturesFromFod = new Button
            {
                Text = "Open Feature Selector",
                Location = new Point(725, 21),
                Size = new Size(150, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.LightSteelBlue,
                Enabled = false
            };
            btnInstallFeaturesFromFod.Click += BtnInstallFeaturesFromFod_Click;

            btnManualCab = new Button
            {
                Text = "Install CAB manually...",
                Location = new Point(890, 21),
                Size = new Size(150, 28),
                BackColor = Color.LightBlue,
                FlatStyle = FlatStyle.Flat
            };
            btnManualCab.Click += BtnManualCab_Click;

            gbFod.Controls.AddRange(new Control[] { lblFod, txtFodIsoPath, btnBrowseFodIso, btnInstallFeaturesFromFod, btnManualCab });

            topPanel.Controls.AddRange(new Control[] { lblKb, txtKbNumber, btnManualInstall, btnRefreshScan, btnSuperDeepScan, chkShowAll, gbFod });

            // DataGridView
            dgvUpdates = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            // Console panel
            Panel bottomPanel = new Panel { Dock = DockStyle.Fill };
            rtbConsole = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 9),
                ReadOnly = true,
                WordWrap = false
            };
            pbScanProgress = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 20,
                Visible = false,
                Style = ProgressBarStyle.Continuous
            };
            lblScanStatus = new Label
            {
                Dock = DockStyle.Top,
                Height = 25,
                Text = "",
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            bottomPanel.Controls.Add(rtbConsole);
            bottomPanel.Controls.Add(pbScanProgress);
            bottomPanel.Controls.Add(lblScanStatus);
            rtbConsole.Dock = DockStyle.Fill;
            pbScanProgress.Dock = DockStyle.Top;
            lblScanStatus.Dock = DockStyle.Top;

            mainLayout.Controls.Add(topPanel, 0, 0);
            mainLayout.Controls.Add(dgvUpdates, 0, 1);
            mainLayout.Controls.Add(bottomPanel, 0, 2);

            this.Controls.Add(mainLayout);
        }

        private void BtnBrowseFodIso_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "Select Features on Demand ISO or WIM";
                ofd.Filter = "ISO files (*.iso)|*.iso|WIM files (*.wim)|*.wim|All files (*.*)|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    txtFodIsoPath.Text = ofd.FileName;
                    btnInstallFeaturesFromFod.Enabled = true;
                }
            }
        }

        private async void BtnInstallFeaturesFromFod_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtFodIsoPath.Text) || !File.Exists(txtFodIsoPath.Text))
            {
                MessageBox.Show("Please select a valid FoD ISO or WIM file first.", "No Source", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrEmpty(_offlineDrive))
            {
                MessageBox.Show("Please click 'Check OS' in the main window first to select an offline drive.", "No Offline Drive", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _scanCts?.Cancel();
            await Task.Delay(500);

            var selectorForm = new FodFeatureSelectorForm(_offlineDrive, txtFodIsoPath.Text);
            selectorForm.ShowDialog(this);
        }

        private async void BtnManualCab_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "Select Windows CAB file to install";
                ofd.Filter = "CAB files (*.cab)|*.cab|All files (*.*)|*.*";
                ofd.Multiselect = false;
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    string cabPath = ofd.FileName;
                    await InstallManualCab(cabPath);
                }
            }
        }

        private async Task InstallManualCab(string cabPath)
        {
            if (_scanCts != null && !_scanCts.IsCancellationRequested)
            {
                AppendToConsole("Cancelling ongoing scan to free DISM...");
                _scanCts.Cancel();
                await Task.Delay(1000);
            }

            SetControlsEnabled(false);

            try
            {
                AppendToConsole($"Installing manual CAB: {cabPath}");
                string args = $"/Image:{_offlineDrive}\\ /Add-Package /PackagePath:\"{cabPath}\" /LimitAccess";
                int exitCode = await RunDismCommandAsync(args, true);
                if (exitCode == 0 || exitCode == 3010)
                {
                    AppendToConsole($"Successfully installed CAB: {Path.GetFileName(cabPath)}");
                    await LoadInstalledUpdatesAsync(false);
                }
                else
                {
                    AppendToConsole($"Failed to install CAB: {Path.GetFileName(cabPath)} (exit {exitCode})");
                }
            }
            catch (Exception ex)
            {
                AppendToConsole($"ERROR installing CAB: {ex.Message}");
            }
            finally
            {
                SetControlsEnabled(true);
            }
        }

        private async Task LoadInstalledUpdatesAsync(bool deepScan)
        {
            try
            {
                rtbConsole.AppendText($"[{DateTime.Now:T}] Starting {(deepScan ? "SUPER DEEP (slow) scan" : "fast scan")}...\r\n");

                _scanCts?.Cancel();
                await Task.Delay(200);
                _scanCts = new CancellationTokenSource();
                var token = _scanCts.Token;

                pbScanProgress.Visible = true;
                lblScanStatus.Visible = true;
                pbScanProgress.Style = ProgressBarStyle.Continuous;
                pbScanProgress.Value = 0;
                lblScanStatus.Text = "Getting package list...";

                string getPackagesOutput = await RunDismGetOutputAsync($"/Image:{_offlineDrive}\\ /Get-Packages /English");
                token.ThrowIfCancellationRequested();

                List<PackageInfo> packages;
                if (deepScan)
                {
                    packages = await DeepScanPackagesAsync(getPackagesOutput, token);
                }
                else
                {
                    packages = ParsePackagesWithKB(getPackagesOutput);
                }

                lblScanStatus.Text = "Scanning Features on Demand...";
                var capabilities = await CapabilityManager.ScanCapabilitiesAsync(_offlineDrive);

                pbScanProgress.Visible = false;
                lblScanStatus.Visible = false;

                DataTable mainTable = new DataTable();
                mainTable.Columns.Add("DisplayName", typeof(string));
                mainTable.Columns.Add("InstallDate", typeof(DateTime));
                mainTable.Columns.Add("Identifier", typeof(string));
                mainTable.Columns.Add("Source", typeof(string));
                mainTable.Columns.Add("State", typeof(string));
                mainTable.Columns.Add("PackageName", typeof(string));

                bool showAll = chkShowAll.Checked;
                foreach (var pkg in packages)
                {
                    if (showAll || IsUpdatableType(pkg.ReleaseType))
                    {
                        DataRow row = mainTable.NewRow();
                        string displayName = pkg.PackageName.Length > 80 ? pkg.PackageName.Substring(0, 80) + "..." : pkg.PackageName;
                        row["DisplayName"] = displayName;
                        row["InstallDate"] = pkg.InstallDate;
                        row["Identifier"] = pkg.KBNumber;
                        row["Source"] = "Package";
                        row["State"] = "Installed";
                        row["PackageName"] = pkg.PackageName;
                        mainTable.Rows.Add(row);
                    }
                }

                foreach (var cap in capabilities)
                {
                    DataRow row = mainTable.NewRow();
                    row["DisplayName"] = cap.DisplayName;
                    row["InstallDate"] = DateTime.Now;
                    row["Identifier"] = cap.CapabilityName;
                    row["Source"] = "FoD";
                    row["State"] = cap.State;
                    row["PackageName"] = cap.CapabilityName;
                    mainTable.Rows.Add(row);
                }

                DataView sortedView = mainTable.DefaultView;
                sortedView.Sort = "InstallDate DESC";
                dgvUpdates.DataSource = sortedView;

                if (!dgvUpdates.Columns.Contains("Uninstall"))
                {
                    DataGridViewButtonColumn btnUninstall = new DataGridViewButtonColumn
                    {
                        Name = "Uninstall",
                        HeaderText = "Action",
                        Text = "Uninstall",
                        UseColumnTextForButtonValue = true,
                        Width = 80
                    };
                    DataGridViewButtonColumn btnReinstall = new DataGridViewButtonColumn
                    {
                        Name = "Reinstall",
                        HeaderText = "",
                        Text = "Reinstall",
                        UseColumnTextForButtonValue = true,
                        Width = 80
                    };
                    dgvUpdates.Columns.Add(btnUninstall);
                    dgvUpdates.Columns.Add(btnReinstall);
                }

                dgvUpdates.CellClick -= DgvUpdates_CellClick;
                dgvUpdates.CellClick += DgvUpdates_CellClick;

                rtbConsole.AppendText($"Loaded {mainTable.Rows.Count} items.\r\n");
            }
            catch (OperationCanceledException)
            {
                rtbConsole.AppendText("Scan cancelled.\r\n");
            }
            catch (Exception ex)
            {
                rtbConsole.AppendText($"ERROR: {ex.Message}\r\n");
                MessageBox.Show($"Failed to load updates: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                pbScanProgress.Visible = false;
                lblScanStatus.Visible = false;
            }
            finally
            {
                _scanCts?.Dispose();
                _scanCts = null;
            }
        }

        private async Task<List<PackageInfo>> DeepScanPackagesAsync(string getPackagesOutput, CancellationToken token)
        {
            List<PackageInfo> shallowPackages = ParseShallowPackagesList(getPackagesOutput);
            List<PackageInfo> enrichedPackages = new List<PackageInfo>();

            pbScanProgress.Maximum = shallowPackages.Count;
            pbScanProgress.Value = 0;

            for (int i = 0; i < shallowPackages.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var pkg = shallowPackages[i];
                lblScanStatus.Text = $"Deep scanning package {i + 1} of {shallowPackages.Count}...";
                pbScanProgress.Value = i + 1;

                string packageInfo = await RunDismGetOutputAsync($"/Image:{_offlineDrive}\\ /Get-PackageInfo /PackageName:\"{pkg.PackageName}\" /English");
                string kbNumber = ExtractKBFromPackageInfo(packageInfo, pkg.PackageName);
                pkg.KBNumber = kbNumber;
                enrichedPackages.Add(pkg);

                await Task.Delay(10, token);
            }
            return enrichedPackages;
        }

        private List<PackageInfo> ParseShallowPackagesList(string dismOutput)
        {
            List<PackageInfo> packages = new List<PackageInfo>();
            var lines = dismOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            string currentIdentity = null;
            string currentRelease = null;
            string currentInstallTime = null;

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("Package Identity :", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentIdentity != null)
                    {
                        AddShallowPackage(packages, currentIdentity, currentRelease, currentInstallTime);
                    }
                    currentIdentity = trimmed.Substring("Package Identity :".Length).Trim();
                    currentRelease = null;
                    currentInstallTime = null;
                }
                else if (trimmed.StartsWith("Release Type :", StringComparison.OrdinalIgnoreCase))
                {
                    currentRelease = trimmed.Substring("Release Type :".Length).Trim();
                }
                else if (trimmed.StartsWith("Install Time :", StringComparison.OrdinalIgnoreCase))
                {
                    currentInstallTime = trimmed.Substring("Install Time :".Length).Trim();
                }
            }
            if (currentIdentity != null)
            {
                AddShallowPackage(packages, currentIdentity, currentRelease, currentInstallTime);
            }
            return packages;
        }

        private void AddShallowPackage(List<PackageInfo> packages, string identity, string releaseType, string installTimeStr)
        {
            DateTime installDate = DateTime.MinValue;
            if (!string.IsNullOrEmpty(installTimeStr))
                DateTime.TryParse(installTimeStr, out installDate);

            packages.Add(new PackageInfo
            {
                PackageName = identity ?? "Unknown",
                InstallDate = installDate,
                ReleaseType = releaseType ?? "Unknown",
                KBNumber = "Unknown"
            });
        }

        private string ExtractKBFromPackageInfo(string packageInfo, string fallbackPackageName)
        {
            Match kbArticle = Regex.Match(packageInfo, @"KB Article\s*:\s*(\d+)", RegexOptions.IgnoreCase);
            if (kbArticle.Success)
                return "KB" + kbArticle.Groups[1].Value;

            Match kbMatch = Regex.Match(packageInfo, @"kbid\s*=\s*(\d+)", RegexOptions.IgnoreCase);
            if (kbMatch.Success)
                return "KB" + kbMatch.Groups[1].Value;

            Match fallback = Regex.Match(fallbackPackageName, @"KB\d{6,8}", RegexOptions.IgnoreCase);
            if (fallback.Success)
                return fallback.Value;

            return "Unknown";
        }

        private List<PackageInfo> ParsePackagesWithKB(string dismOutput)
        {
            List<PackageInfo> packages = new List<PackageInfo>();
            var lines = dismOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            string currentIdentity = null;
            string currentRelease = null;
            string currentInstallTime = null;

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("Package Identity :", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentIdentity != null)
                    {
                        AddPackageFast(packages, currentIdentity, currentRelease, currentInstallTime);
                    }
                    currentIdentity = trimmed.Substring("Package Identity :".Length).Trim();
                    currentRelease = null;
                    currentInstallTime = null;
                }
                else if (trimmed.StartsWith("Release Type :", StringComparison.OrdinalIgnoreCase))
                {
                    currentRelease = trimmed.Substring("Release Type :".Length).Trim();
                }
                else if (trimmed.StartsWith("Install Time :", StringComparison.OrdinalIgnoreCase))
                {
                    currentInstallTime = trimmed.Substring("Install Time :".Length).Trim();
                }
            }
            if (currentIdentity != null)
            {
                AddPackageFast(packages, currentIdentity, currentRelease, currentInstallTime);
            }
            return packages;
        }

        private void AddPackageFast(List<PackageInfo> packages, string identity, string releaseType, string installTimeStr)
        {
            DateTime installDate = DateTime.MinValue;
            if (!string.IsNullOrEmpty(installTimeStr))
                DateTime.TryParse(installTimeStr, out installDate);

            string kbNumber = "Unknown";
            if (!string.IsNullOrEmpty(identity))
            {
                Match kbMatch = Regex.Match(identity, @"KB\d{6,8}", RegexOptions.IgnoreCase);
                if (kbMatch.Success)
                    kbNumber = kbMatch.Value;
                else
                {
                    Match numMatch = Regex.Match(identity, @"\b(\d{6,7})\b");
                    if (numMatch.Success)
                        kbNumber = "KB" + numMatch.Groups[1].Value;
                    else
                    {
                        Match buildMatch = Regex.Match(identity, @"\b(\d+\.\d+\.\d+\.\d+)\b");
                        if (buildMatch.Success)
                            kbNumber = "Build " + buildMatch.Groups[1].Value;
                    }
                }
            }

            packages.Add(new PackageInfo
            {
                PackageName = identity ?? "Unknown",
                InstallDate = installDate,
                ReleaseType = releaseType ?? "Unknown",
                KBNumber = kbNumber
            });
        }

        private bool IsUpdatableType(string releaseType)
        {
            string[] allowed = { "Update", "Security Update", "Rollup", "Hotfix" };
            foreach (var a in allowed)
                if (string.Equals(releaseType, a, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private async void DgvUpdates_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            DataRowView row = (DataRowView)dgvUpdates.Rows[e.RowIndex].DataBoundItem;
            string identifier = row["Identifier"].ToString();
            string source = row["Source"].ToString();
            string packageName = row["PackageName"].ToString();

            if (dgvUpdates.Columns[e.ColumnIndex].Name == "Uninstall")
            {
                if (source == "Package")
                    await UninstallPackageAsync(packageName, identifier, e.RowIndex);
                else if (source == "FoD")
                    await UninstallCapabilityAsync(identifier);
            }
            else if (dgvUpdates.Columns[e.ColumnIndex].Name == "Reinstall")
            {
                if (source == "Package")
                    await ReinstallPackageAsync(packageName, identifier, e.RowIndex);
                else if (source == "FoD")
                    await ReinstallCapabilityAsync(identifier);
            }
        }

        private async void BtnManualInstall_Click(object sender, EventArgs e)
        {
            string kb = txtKbNumber.Text.Trim();
            if (string.IsNullOrEmpty(kb))
            {
                MessageBox.Show("Enter a KB number (e.g., KB5031445).", "Missing KB", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult result = MessageBox.Show(
                "WARNING: Installing updates manually without checking prerequisites (like Servicing Stack Updates) can cause unbootable states or system instability.\n\nAre you sure you want to force install this update?",
                "Force Install?",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes) return;

            await RunUpdateOperation(async () =>
            {
                string downloadedFile = await MSCatalogWrapper.DownloadUpdateAsync(kb);
                AppendToConsole($"Downloaded: {downloadedFile}");

                string installArgs = $"/Image:{_offlineDrive}\\ /Add-Package /PackagePath:\"{downloadedFile}\"";
                await RunDismWithLogging(installArgs, $"Installing {kb}");

                await LoadInstalledUpdatesAsync(false);
            }, "Manual Install");
        }

        private async Task UninstallPackageAsync(string packageName, string kbNumber, int rowIndex)
        {
            if (MessageBox.Show($"Uninstall {kbNumber}?\n\nThis action may require a reboot.", "Confirm Uninstall",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            await RunUpdateOperation(async () =>
            {
                string args = $"/Image:{_offlineDrive}\\ /Remove-Package /PackageName:\"{packageName}\"";
                await RunDismWithLogging(args, $"Uninstalling {kbNumber}");
                dgvUpdates.Invoke(new Action(() =>
                {
                    ((DataView)dgvUpdates.DataSource).Table.Rows[rowIndex].Delete();
                }));
            }, "Uninstall Package");
        }

        private async Task ReinstallPackageAsync(string packageName, string kbNumber, int rowIndex)
        {
            if (!kbNumber.StartsWith("KB", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("This update does not have a standard KB number. Please use the Manual Install box.", "Cannot Reinstall", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show($"Reinstall {kbNumber}? This will attempt to reinstall the update.", "Confirm Reinstall",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            await RunUpdateOperation(async () =>
            {
                string downloadedFile = await MSCatalogWrapper.DownloadUpdateAsync(kbNumber);
                AppendToConsole($"Downloaded: {downloadedFile}");

                try
                {
                    string uninstallArgs = $"/Image:{_offlineDrive}\\ /Remove-Package /PackageName:\"{packageName}\"";
                    await RunDismWithLogging(uninstallArgs, $"Uninstalling old {kbNumber}");
                }
                catch (Exception ex)
                {
                    AppendToConsole($"Uninstall skipped or failed: {ex.Message}");
                }

                string installArgs = $"/Image:{_offlineDrive}\\ /Add-Package /PackagePath:\"{downloadedFile}\"";
                await RunDismWithLogging(installArgs, $"Installing {kbNumber}");

                await LoadInstalledUpdatesAsync(false);
            }, "Reinstall Package");
        }

        private async Task UninstallCapabilityAsync(string capabilityName)
        {
            if (MessageBox.Show($"Uninstall capability '{capabilityName}'?\n\nThis action may require a reboot.", "Confirm Uninstall",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            await RunUpdateOperation(async () =>
            {
                bool success = await CapabilityManager.UninstallCapabilityAsync(_offlineDrive, capabilityName, AppendToConsole);
                if (success)
                {
                    AppendToConsole($"Successfully uninstalled capability: {capabilityName}");
                    await LoadInstalledUpdatesAsync(false);
                }
                else
                    AppendToConsole($"Failed to uninstall capability: {capabilityName}");
            }, "Uninstall Capability");
        }

        private async Task ReinstallCapabilityAsync(string capabilityName)
        {
            if (string.IsNullOrEmpty(txtFodIsoPath.Text) || !File.Exists(txtFodIsoPath.Text))
            {
                MessageBox.Show("Please provide a valid Features on Demand (FoD) ISO file path.", "No FOD ISO", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (MessageBox.Show($"Reinstall capability '{capabilityName}'? This will uninstall (if possible) and reinstall the feature.", "Confirm Reinstall",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            await RunUpdateOperation(async () =>
            {
                string mountPoint = null;
                try
                {
                    mountPoint = await MountIsoAsync(txtFodIsoPath.Text);
                    if (string.IsNullOrEmpty(mountPoint))
                        throw new Exception("Failed to mount FoD ISO.");

                    AppendToConsole($"Mounted ISO to {mountPoint}");
                    string sourceFolder = await FindSourceFolder(mountPoint);
                    if (string.IsNullOrEmpty(sourceFolder))
                        sourceFolder = await GetSourceFolderManual();

                    await CapabilityManager.UninstallCapabilityAsync(_offlineDrive, capabilityName, AppendToConsole);
                    bool success = await CapabilityManager.InstallCapabilityAsync(_offlineDrive, capabilityName, sourceFolder, AppendToConsole);
                    if (success)
                    {
                        AppendToConsole($"Successfully reinstalled capability: {capabilityName}");
                        await LoadInstalledUpdatesAsync(false);
                    }
                    else
                        AppendToConsole($"Failed to reinstall capability: {capabilityName}");
                }
                finally
                {
                    if (!string.IsNullOrEmpty(mountPoint))
                        await DismountIsoAsync(mountPoint);
                }
            }, "Reinstall Capability");
        }

        private async Task<string> MountIsoAsync(string isoPath)
        {
            return await Task.Run(() =>
            {
                using (Process p = new Process())
                {
                    p.StartInfo.FileName = "powershell.exe";
                    p.StartInfo.Arguments = $"-Command \"$drive = (Mount-DiskImage -ImagePath '{isoPath}' -PassThru | Get-Volume).DriveLetter; if ($drive) {{ $drive + ':\\' }} else {{ $null }}\"";
                    p.StartInfo.CreateNoWindow = true;
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.Start();
                    string output = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit();
                    if (!string.IsNullOrEmpty(output) && output.Length >= 1 && char.IsLetter(output[0]))
                        return output[0] + ":\\";
                    return null;
                }
            });
        }

        private async Task DismountIsoAsync(string driveLetter)
        {
            await Task.Run(() =>
            {
                using (Process p = new Process())
                {
                    p.StartInfo.FileName = "powershell.exe";
                    p.StartInfo.Arguments = $"-Command \"Get-DiskImage | Where-Object {{ $_.DevicePath -like '*{driveLetter.TrimEnd('\\')}*' }} | Dismount-DiskImage\"";
                    p.StartInfo.CreateNoWindow = true;
                    p.StartInfo.UseShellExecute = false;
                    p.Start();
                    p.WaitForExit();
                }
            });
        }

        private async Task<string> FindSourceFolder(string driveRoot)
        {
            string[] possiblePaths = {
                driveRoot,
                Path.Combine(driveRoot, "sources"),
                Path.Combine(driveRoot, "sources", "sxs"),
                Path.Combine(driveRoot, "Packages")
            };
            foreach (string path in possiblePaths)
            {
                if (Directory.Exists(path) && (Directory.GetFiles(path, "*.cab").Length > 0 || Directory.GetFiles(path, "*.cab", SearchOption.AllDirectories).Length > 0))
                {
                    AppendToConsole($"Auto-detected source folder: {path}");
                    return path;
                }
            }
            return null;
        }

        private async Task<string> GetSourceFolderManual()
        {
            AppendToConsole("Could not auto-detect FoD source folder. Please select the folder containing cab files.");
            string selected = null;
            await Task.Run(() =>
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "Select the folder containing the FoD cab files (e.g., sources\\sxs)";
                    if (dialog.ShowDialog() == DialogResult.OK)
                        selected = dialog.SelectedPath;
                }
            });
            return selected;
        }

        private void ClearCbsPendingState()
        {
            try
            {
                AppendToConsole("[ClearCBS] Attempting to clear pending CBS state...");
                RunHiddenCommand("net stop wuauserv /y", false);
                RunHiddenCommand("net stop trustedinstaller /y", false);
                RunHiddenCommand("net stop cryptsvc /y", false);
                RunHiddenCommand("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Component Based Servicing\\SessionsPending\" /v Exclusive /t REG_DWORD /d 0 /f", true);
                RunHiddenCommand("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Component Based Servicing\\SessionsPending\" /v TotalSessionPhases /t REG_DWORD /d 0 /f", true);
                string pendingXml = @"C:\Windows\WinSxS\pending.xml";
                if (File.Exists(pendingXml))
                {
                    RunHiddenCommand($"takeown /f \"{pendingXml}\"", true);
                    RunHiddenCommand($"icacls \"{pendingXml}\" /grant Everyone:F", true);
                    RunHiddenCommand($"del /f \"{pendingXml}\"", true);
                }
                AppendToConsole("[ClearCBS] CBS pending state cleared successfully.");
            }
            catch (Exception ex)
            {
                AppendToConsole($"[ClearCBS] Warning: {ex.Message}");
            }
        }

        private void RunHiddenCommand(string command, bool waitForExit)
        {
            using (Process p = new Process())
            {
                p.StartInfo.FileName = "cmd.exe";
                p.StartInfo.Arguments = $"/c {command}";
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.UseShellExecute = false;
                if (waitForExit)
                    p.StartInfo.RedirectStandardOutput = true;
                p.Start();
                if (waitForExit)
                    p.WaitForExit(5000);
            }
        }

        private async Task RunDismWithLogging(string arguments, string stepDescription)
        {
            ClearCbsPendingState();
            AppendToConsole($"\r\n[{stepDescription}] Running: dism.exe {arguments}");
            int exitCode = await RunDismCommandAsync(arguments, true);
            if (exitCode == 0 || exitCode == 3010)
            {
                if (exitCode == 3010)
                    AppendToConsole("DISM completed with exit code 3010 (reboot required).");
                else
                    AppendToConsole("DISM completed successfully.");
            }
            else
            {
                throw new Exception($"DISM failed with exit code {exitCode}");
            }
        }

        private Task<int> RunDismCommandAsync(string arguments, bool realTimeLogging = false)
        {
            return Task.Run(() =>
            {
                using (Process p = new Process())
                {
                    p.StartInfo.FileName = "dism.exe";
                    p.StartInfo.Arguments = arguments;
                    p.StartInfo.CreateNoWindow = true;
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.RedirectStandardError = true;
                    p.EnableRaisingEvents = true;

                    if (realTimeLogging)
                    {
                        p.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) AppendToConsole(e.Data); };
                        p.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) AppendToConsole("[ERROR] " + e.Data); };
                    }

                    p.Start();
                    if (realTimeLogging)
                    {
                        p.BeginOutputReadLine();
                        p.BeginErrorReadLine();
                    }
                    p.WaitForExit();

                    if (!realTimeLogging)
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        string error = p.StandardError.ReadToEnd();
                        AppendToConsole(output);
                        if (!string.IsNullOrEmpty(error))
                            AppendToConsole("[ERROR] " + error);
                    }
                    return p.ExitCode;
                }
            });
        }

        private async Task<string> RunDismGetOutputAsync(string arguments)
        {
            return await Task.Run(() =>
            {
                using (Process p = new Process())
                {
                    p.StartInfo.FileName = "dism.exe";
                    p.StartInfo.Arguments = arguments;
                    p.StartInfo.CreateNoWindow = true;
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.Start();
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    return output;
                }
            });
        }

        private void SetControlsEnabled(bool enabled)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<bool>(SetControlsEnabled), enabled);
                return;
            }
            dgvUpdates.Enabled = enabled;
            btnManualInstall.Enabled = enabled;
            btnRefreshScan.Enabled = enabled;
            btnSuperDeepScan.Enabled = enabled;
            btnManualCab.Enabled = enabled;
            txtKbNumber.Enabled = enabled;
            chkShowAll.Enabled = enabled;
            txtFodIsoPath.Enabled = enabled;
            btnBrowseFodIso.Enabled = enabled;
            btnInstallFeaturesFromFod.Enabled = !string.IsNullOrEmpty(txtFodIsoPath.Text) && File.Exists(txtFodIsoPath.Text);
        }

        private async Task RunUpdateOperation(Func<Task> operation, string operationName)
        {
            SetControlsEnabled(false);
            try
            {
                await operation();
                AppendToConsole($"\r\n[{operationName}] Completed successfully.");
            }
            catch (Exception ex)
            {
                AppendToConsole($"\r\n[{operationName}] FAILED: {ex.Message}");
                MessageBox.Show($"Operation failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetControlsEnabled(true);
            }
        }

        private void AppendToConsole(string text)
        {
            if (rtbConsole.InvokeRequired)
            {
                rtbConsole.Invoke(new Action<string>(AppendToConsole), text);
                return;
            }
            rtbConsole.AppendText(text + "\r\n");
            rtbConsole.ScrollToCaret();
        }
    }
}