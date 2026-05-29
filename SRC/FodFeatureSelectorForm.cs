using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DimSim_Windows_Repair
{
    public class FodFeatureSelectorForm : Form
    {
        private readonly string _offlineDrive;
        private readonly string _fodSourcePath;
        private DataGridView dgvFeatures;
        private RichTextBox rtbConsole;
        private Button btnRefresh;
        private Button btnInstallSelected;
        private Button btnManualCab;
        private ProgressBar pbProgress;
        private Label lblStatus;
        private DataTable featuresTable;
        private string _mountedDrive = null;
        private string _effectiveSource = null;
        private Dictionary<string, List<string>> _featureLanguages = new Dictionary<string, List<string>>();
        
        // Track whether we mounted the ISO (so we only unmount if we did)
        private bool _weMountedIso = false;
        private string _preMountedDrive = null;

        public FodFeatureSelectorForm(string offlineDrive, string fodSourcePath)
        {
            _offlineDrive = offlineDrive;
            _fodSourcePath = fodSourcePath;
            InitializeComponent();
            this.Load += async (s, e) => await LoadFeaturesFromIsoAsync();
            this.FormClosing += async (s, e) => await DismountIsoAsync();
        }

        private void InitializeComponent()
        {
            this.Text = $"Install Features from FoD - Target: {_offlineDrive}";
            this.Size = new Size(1100, 700);
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimumSize = new Size(900, 500);

            TableLayoutPanel mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(5)
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));

            Panel topPanel = new Panel { Dock = DockStyle.Fill, Height = 40 };
            btnRefresh = new Button
            {
                Text = "Refresh List",
                Location = new Point(10, 8),
                Size = new Size(100, 28),
                FlatStyle = FlatStyle.Flat
            };
            btnInstallSelected = new Button
            {
                Text = "Install Selected Feature(s)",
                Location = new Point(120, 8),
                Size = new Size(180, 28),
                BackColor = Color.LightGreen,
                FlatStyle = FlatStyle.Flat,
                Enabled = false
            };
            btnManualCab = new Button
            {
                Text = "Install CAB manually...",
                Location = new Point(310, 8),
                Size = new Size(160, 28),
                BackColor = Color.LightBlue,
                FlatStyle = FlatStyle.Flat
            };
            Label lblSource = new Label
            {
                Text = $"FoD Source: {_fodSourcePath}",
                Location = new Point(490, 12),
                Size = new Size(600, 23),
                Font = new Font("Segoe UI", 8, FontStyle.Italic),
                ForeColor = Color.DarkBlue
            };
            topPanel.Controls.AddRange(new Control[] { btnRefresh, btnInstallSelected, btnManualCab, lblSource });

            dgvFeatures = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true
            };

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
            pbProgress = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 20,
                Visible = false,
                Style = ProgressBarStyle.Continuous
            };
            lblStatus = new Label
            {
                Dock = DockStyle.Top,
                Height = 25,
                Text = "",
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            bottomPanel.Controls.Add(rtbConsole);
            bottomPanel.Controls.Add(pbProgress);
            bottomPanel.Controls.Add(lblStatus);
            rtbConsole.Dock = DockStyle.Fill;
            pbProgress.Dock = DockStyle.Top;
            lblStatus.Dock = DockStyle.Top;

            mainLayout.Controls.Add(topPanel, 0, 0);
            mainLayout.Controls.Add(dgvFeatures, 0, 1);
            mainLayout.Controls.Add(bottomPanel, 0, 2);

            this.Controls.Add(mainLayout);

            btnRefresh.Click += async (s, e) => await LoadFeaturesFromIsoAsync();
            btnInstallSelected.Click += BtnInstallSelected_Click;
            btnManualCab.Click += BtnManualCab_Click;
            dgvFeatures.SelectionChanged += (s, e) => btnInstallSelected.Enabled = dgvFeatures.SelectedRows.Count > 0;
            dgvFeatures.CellClick += DgvFeatures_CellClick;
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
            btnRefresh.Enabled = false;
            btnInstallSelected.Enabled = false;
            btnManualCab.Enabled = false;
            dgvFeatures.Enabled = false;

            try
            {
                AppendToConsole($"Installing manual CAB: {cabPath}");
                string args = $"/Image:{_offlineDrive}\\ /Add-Package /PackagePath:\"{cabPath}\" /LimitAccess";
                int exitCode = await RunDismCommandAsync(args, true);
                if (exitCode == 0 || exitCode == 3010)
                {
                    AppendToConsole($"Successfully installed CAB: {Path.GetFileName(cabPath)}");
                    // Optionally refresh the feature list after manual install
                    await LoadFeaturesFromIsoAsync();
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
                btnRefresh.Enabled = true;
                btnInstallSelected.Enabled = dgvFeatures.SelectedRows.Count > 0;
                btnManualCab.Enabled = true;
                dgvFeatures.Enabled = true;
            }
        }

        private async Task LoadFeaturesFromIsoAsync()
        {
            try
            {
                btnRefresh.Enabled = false;
                pbProgress.Visible = true;
                lblStatus.Visible = true;
                lblStatus.Text = "Preparing FoD source...";
                pbProgress.Style = ProgressBarStyle.Marquee;

                await PrepareSourceAsync();

                pbProgress.Style = ProgressBarStyle.Continuous;
                lblStatus.Text = "Scanning cab files...";
                pbProgress.Value = 0;

                _featureLanguages.Clear();
                await LoadAndGroupFeatures();

                featuresTable = new DataTable();
                featuresTable.Columns.Add("FeatureName", typeof(string));
                featuresTable.Columns.Add("Languages", typeof(string));
                featuresTable.Columns.Add("BaseName", typeof(string)); // hidden

                foreach (var kvp in _featureLanguages)
                {
                    string languages = string.Join(", ", kvp.Value);
                    featuresTable.Rows.Add(kvp.Key, languages, kvp.Key);
                }

                dgvFeatures.DataSource = featuresTable;
                dgvFeatures.Columns["BaseName"].Visible = false;

                if (!dgvFeatures.Columns.Contains("Install"))
                {
                    DataGridViewButtonColumn btnInstall = new DataGridViewButtonColumn
                    {
                        Name = "Install",
                        HeaderText = "Action",
                        Text = "Install",
                        UseColumnTextForButtonValue = true,
                        Width = 80
                    };
                    dgvFeatures.Columns.Add(btnInstall);
                }

                // Prevent double event subscription
                dgvFeatures.CellClick -= DgvFeatures_CellClick;
                dgvFeatures.CellClick += DgvFeatures_CellClick;

                AppendToConsole($"Found {_featureLanguages.Count} unique features (grouped).");
                pbProgress.Visible = false;
                lblStatus.Visible = false;
                btnRefresh.Enabled = true;
            }
            catch (Exception ex)
            {
                AppendToConsole($"ERROR: {ex.Message}");
                MessageBox.Show($"Failed to load features: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                pbProgress.Visible = false;
                lblStatus.Visible = false;
                btnRefresh.Enabled = true;
            }
        }

        private async Task PrepareSourceAsync()
        {
            if (!string.IsNullOrEmpty(_effectiveSource))
                return;

            if (_fodSourcePath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
            {
                // Check if ISO is already mounted
                _preMountedDrive = await GetMountedDriveForIsoAsync(_fodSourcePath);
                if (!string.IsNullOrEmpty(_preMountedDrive))
                {
                    AppendToConsole($"ISO already mounted to {_preMountedDrive}");
                    _mountedDrive = _preMountedDrive;
                    _weMountedIso = false;
                }
                else
                {
                    _mountedDrive = await MountIsoAsync(_fodSourcePath);
                    _weMountedIso = true;
                }

                if (string.IsNullOrEmpty(_mountedDrive))
                    throw new Exception("Failed to mount ISO.");

                AppendToConsole($"Using ISO mounted at {_mountedDrive}");
                _effectiveSource = FindSourceFolder(_mountedDrive);
            }
            else if (Directory.Exists(_fodSourcePath))
            {
                _effectiveSource = FindSourceFolder(_fodSourcePath);
            }
            else
            {
                throw new Exception("Source path is not a valid ISO or folder.");
            }

            if (string.IsNullOrEmpty(_effectiveSource))
                throw new Exception("Could not find a folder containing cab files.");
            AppendToConsole($"Using source folder: {_effectiveSource}");
        }

        private async Task<string> GetMountedDriveForIsoAsync(string isoPath)
        {
            return await Task.Run(() =>
            {
                using (Process p = new Process())
                {
                    p.StartInfo.FileName = "powershell.exe";
                    p.StartInfo.Arguments = $"-Command \"(Get-DiskImage -ImagePath '{isoPath}' | Get-Volume).DriveLetter\"";
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

        private string FindSourceFolder(string root)
        {
            string[] possiblePaths = {
                Path.Combine(root, "LanguagesAndOptionalFeatures"),
                Path.Combine(root, "sources", "sxs"),
                root
            };
            foreach (string path in possiblePaths)
            {
                if (Directory.Exists(path))
                {
                    if (Directory.GetFiles(path, "*.cab", SearchOption.TopDirectoryOnly).Length > 0 ||
                        Directory.GetFiles(path, "*.cab", SearchOption.AllDirectories).Length > 0)
                    {
                        return path;
                    }
                }
            }
            return null;
        }

        private bool IsValidFeatureCab(string cabPath)
        {
            string fileName = Path.GetFileNameWithoutExtension(cabPath);
            
            // Skip known metadata/CompDB files (case-insensitive)
            string lowerName = fileName.ToLowerInvariant();
            if (lowerName.Contains("compdb") || lowerName.Contains("metadata") || lowerName.Contains("desktoptargetcompdb"))
                return false;
            
            // Valid feature cabs usually have at least three tilde-separated parts
            string[] parts = fileName.Split('~');
            return parts.Length >= 3;
        }

        private async Task LoadAndGroupFeatures()
        {
            var allCabFiles = Directory.GetFiles(_effectiveSource, "*.cab", SearchOption.AllDirectories);
            var cabFiles = allCabFiles.Where(IsValidFeatureCab).ToArray();
            
            if (cabFiles.Length == 0)
            {
                AppendToConsole("Warning: No valid feature cab files found after filtering.");
                return;
            }
            
            pbProgress.Maximum = cabFiles.Length;
            pbProgress.Value = 0;

            for (int i = 0; i < cabFiles.Length; i++)
            {
                string cab = cabFiles[i];
                pbProgress.Value = i + 1;
                lblStatus.Text = $"Scanning {Path.GetFileName(cab)}...";

                string fileName = Path.GetFileNameWithoutExtension(cab);
                string baseName = fileName;
                string language = "neutral";

                // Split by tilde (CBS package naming)
                string[] parts = fileName.Split('~');
                if (parts.Length >= 3)
                {
                    // Base name is first three parts: Package~PublicKeyToken~Architecture
                    baseName = $"{parts[0]}~{parts[1]}~{parts[2]}";
                    // Language is the 4th part (index 3)
                    if (parts.Length > 3)
                        language = string.IsNullOrEmpty(parts[3]) ? "neutral" : parts[3];
                }
                else
                {
                    // Fallback: try to extract language as last segment after a tilde or dash
                    var langMatch = Regex.Match(fileName, @"[-~]([a-z]{2}-[A-Z]{2}|[a-z]{2,3})$", RegexOptions.IgnoreCase);
                    if (langMatch.Success)
                    {
                        language = langMatch.Groups[1].Value;
                        baseName = fileName.Substring(0, langMatch.Index);
                    }
                }

                if (!_featureLanguages.ContainsKey(baseName))
                    _featureLanguages[baseName] = new List<string>();
                if (!_featureLanguages[baseName].Contains(language))
                    _featureLanguages[baseName].Add(language);

                if (i % 10 == 0) await Task.Delay(1);
            }
        }

        private async void DgvFeatures_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            // Ignore clicks on row/column headers
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.ColumnIndex >= dgvFeatures.Columns.Count)
                return;

            // Check if the clicked column is the "Install" button column
            if (dgvFeatures.Columns[e.ColumnIndex].Name == "Install")
            {
                // Ensure the "BaseName" column exists and the row's cell is not null
                if (!dgvFeatures.Columns.Contains("BaseName"))
                {
                    AppendToConsole("ERROR: 'BaseName' column not found.");
                    return;
                }

                DataGridViewCell baseNameCell = dgvFeatures.Rows[e.RowIndex].Cells["BaseName"];
                if (baseNameCell.Value == null)
                {
                    AppendToConsole("ERROR: Cannot retrieve base name for selected feature.");
                    return;
                }

                string baseName = baseNameCell.Value.ToString();
                if (string.IsNullOrEmpty(baseName))
                {
                    AppendToConsole("ERROR: Base name is empty.");
                    return;
                }

                await ShowLanguageSelectionAndInstall(baseName);
            }
        }

        private async Task ShowLanguageSelectionAndInstall(string baseName)
        {
            List<string> languages = _featureLanguages.ContainsKey(baseName) ? _featureLanguages[baseName] : new List<string>();
            if (languages.Count == 0) return;

            if (languages.Count == 1 && (languages[0] == "neutral" || languages[0] == "default"))
            {
                await InstallFeature(baseName, null);
                return;
            }

            using (Form dialog = new Form())
            {
                dialog.Text = $"Select Language for {baseName}";
                dialog.Size = new Size(400, 150);
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;

                Label lbl = new Label { Text = "Choose language:", Location = new Point(20, 20), Size = new Size(100, 25) };
                ComboBox cmb = new ComboBox { Location = new Point(130, 18), Size = new Size(200, 25), DropDownStyle = ComboBoxStyle.DropDownList };
                cmb.Items.AddRange(languages.ToArray());
                cmb.SelectedIndex = 0;
                Button btnInstall = new Button { Text = "Install", Location = new Point(130, 60), Size = new Size(80, 30), DialogResult = DialogResult.OK };
                Button btnCancel = new Button { Text = "Cancel", Location = new Point(220, 60), Size = new Size(80, 30), DialogResult = DialogResult.Cancel };
                dialog.Controls.Add(lbl);
                dialog.Controls.Add(cmb);
                dialog.Controls.Add(btnInstall);
                dialog.Controls.Add(btnCancel);

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    string selectedLanguage = cmb.SelectedItem.ToString();
                    await InstallFeature(baseName, selectedLanguage);
                }
            }
        }

        private async Task InstallFeature(string baseName, string language)
        {
            btnInstallSelected.Enabled = false;
            btnRefresh.Enabled = false;
            btnManualCab.Enabled = false;
            dgvFeatures.Enabled = false;

            try
            {
                // Reconstruct the cab file pattern
                string searchPattern = $"{baseName}*.cab";
                if (language != null && language != "neutral" && language != "default")
                    searchPattern = $"{baseName}~{language}*.cab";
                else if (language == "neutral")
                    searchPattern = $"{baseName}~~*.cab";

                var cabFiles = Directory.GetFiles(_effectiveSource, searchPattern, SearchOption.AllDirectories);
                if (cabFiles.Length == 0)
                {
                    // Try a more flexible search: any cab containing the base name and language
                    cabFiles = Directory.GetFiles(_effectiveSource, "*.cab", SearchOption.AllDirectories)
                        .Where(f => f.Contains(baseName) && (language == null || f.Contains(language)))
                        .ToArray();
                }

                if (cabFiles.Length == 0)
                {
                    AppendToConsole($"Could not find cab file for {baseName} (lang: {language ?? "default"})");
                    return;
                }

                string cabPath = cabFiles[0];
                AppendToConsole($"Installing feature from {cabPath}");
                string args = $"/Image:{_offlineDrive}\\ /Add-Package /PackagePath:\"{cabPath}\" /LimitAccess";
                int exitCode = await RunDismCommandAsync(args, true);
                if (exitCode == 0 || exitCode == 3010)
                {
                    AppendToConsole($"Successfully installed {baseName} ({language ?? "default"})");
                    // Refresh the list after installation
                    await LoadFeaturesFromIsoAsync();
                }
                else
                    AppendToConsole($"Failed to install {baseName} ({language ?? "default"}) (exit {exitCode})");
            }
            catch (Exception ex)
            {
                AppendToConsole($"ERROR installing {baseName}: {ex.Message}");
            }
            finally
            {
                btnInstallSelected.Enabled = dgvFeatures.SelectedRows.Count > 0;
                btnRefresh.Enabled = true;
                btnManualCab.Enabled = true;
                dgvFeatures.Enabled = true;
            }
        }

        private async void BtnInstallSelected_Click(object sender, EventArgs e)
        {
            var selectedRows = new List<DataGridViewRow>();
            foreach (DataGridViewRow row in dgvFeatures.SelectedRows)
                selectedRows.Add(row);

            if (selectedRows.Count == 0) return;

            DialogResult confirm = MessageBox.Show($"Install {selectedRows.Count} selected feature(s)?\n\nFor each feature you will select a language.", "Confirm Installation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            foreach (var row in selectedRows)
            {
                string baseName = row.Cells["BaseName"].Value.ToString();
                await ShowLanguageSelectionAndInstall(baseName);
            }
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

        private async Task DismountIsoAsync()
        {
            if (_weMountedIso && !string.IsNullOrEmpty(_mountedDrive))
            {
                await Task.Run(() =>
                {
                    using (Process p = new Process())
                    {
                        p.StartInfo.FileName = "powershell.exe";
                        p.StartInfo.Arguments = $"-Command \"Get-DiskImage | Where-Object {{ $_.DevicePath -like '*{_mountedDrive.TrimEnd('\\')}*' }} | Dismount-DiskImage\"";
                        p.StartInfo.CreateNoWindow = true;
                        p.StartInfo.UseShellExecute = false;
                        p.Start();
                        p.WaitForExit();
                    }
                });
                _mountedDrive = null;
                _weMountedIso = false;
            }
        }

        private Task<int> RunDismCommandAsync(string arguments, bool realTimeLogging)
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
                    return p.ExitCode;
                }
            });
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