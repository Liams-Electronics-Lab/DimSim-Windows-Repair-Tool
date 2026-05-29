using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DimSim_Windows_Repair
{
    public partial class BCDManagementForm : Form
    {
        private string offlineDrive;
        private string bcdStorePath;
        private bool isBcdAvailable = false;
        private string debugLogPath;

        private DataGridView dgvEntries;
        private NumericUpDown nudTimeout;
        private Label lblStatus;
        private Button btnRefresh, btnBackup, btnLoadBackup, btnSaveUnmount, btnRepairBcd, btnClose;

        private List<BcdEntry> bcdEntries;

        public BCDManagementForm(string drive)
        {
            offlineDrive = drive;
            debugLogPath = Path.Combine(Application.StartupPath, "bcd_debug.log");
            InitializeComponent();
            this.Load += BCDManagementForm_Load;
        }

        private void InitializeComponent()
        {
            this.Text = "BCD Management - " + offlineDrive;
            this.Size = new Size(1300, 750);
            this.StartPosition = FormStartPosition.CenterParent;

            TableLayoutPanel mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new Padding(10)
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));

            Label lblTimeout = new Label { Text = "Boot Menu Timeout (seconds):", TextAlign = ContentAlignment.MiddleLeft, AutoSize = true };
            nudTimeout = new NumericUpDown { Minimum = 0, Maximum = 999, Value = 30, Width = 80 };
            FlowLayoutPanel timeoutPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
            timeoutPanel.Controls.Add(lblTimeout);
            timeoutPanel.Controls.Add(nudTimeout);
            nudTimeout.ValueChanged += NudTimeout_ValueChanged;
            mainLayout.Controls.Add(timeoutPanel, 0, 0);

            FlowLayoutPanel buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(0)
            };
            btnBackup = new Button { Text = "Backup BCD", Size = new Size(100, 32) };
            btnLoadBackup = new Button { Text = "Load Backup", Size = new Size(110, 32) };
            btnRefresh = new Button { Text = "Refresh", Size = new Size(80, 32) };
            btnRepairBcd = new Button { Text = "Repair BCD", Size = new Size(120, 32) };
            btnSaveUnmount = new Button { Text = "Save & Unmount", Size = new Size(120, 32) };
            btnClose = new Button { Text = "Close", Size = new Size(80, 32) };

            buttonPanel.Controls.Add(btnBackup);
            buttonPanel.Controls.Add(btnLoadBackup);
            buttonPanel.Controls.Add(btnRefresh);
            buttonPanel.Controls.Add(btnRepairBcd);
            buttonPanel.Controls.Add(btnSaveUnmount);
            buttonPanel.Controls.Add(btnClose);

            mainLayout.Controls.Add(buttonPanel, 1, 0);

            dgvEntries = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            dgvEntries.DoubleClick += DgvEntries_DoubleClick;
            mainLayout.Controls.Add(dgvEntries, 0, 1);
            mainLayout.SetColumnSpan(dgvEntries, 2);

            lblStatus = new Label { Text = "Ready.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            mainLayout.Controls.Add(lblStatus, 0, 2);
            mainLayout.SetColumnSpan(lblStatus, 2);

            this.Controls.Add(mainLayout);

            btnBackup.Click += BtnBackup_Click;
            btnLoadBackup.Click += BtnLoadBackup_Click;
            btnRefresh.Click += BtnRefresh_Click;
            btnRepairBcd.Click += BtnRepairBcd_Click;
            btnSaveUnmount.Click += (s, e) => this.Close();
            btnClose.Click += (s, e) => this.Close();
        }

        #region BCD Location
        private void WriteDebug(string message)
        {
            try { File.AppendAllText(debugLogPath, $"{DateTime.Now}: {message}\r\n"); } catch { }
        }

        private void LocateBcdStore()
        {
            WriteDebug($"Locating BCD for drive: {offlineDrive}");
            string simplePath = Path.Combine(offlineDrive, "Boot", "BCD");
            if (File.Exists(simplePath))
            {
                bcdStorePath = simplePath;
                isBcdAvailable = true;
                WriteDebug($"Found BIOS BCD: {bcdStorePath}");
                return;
            }
            string efiBcdPath = GetEfiBcdPathViaPowerShell();
            if (!string.IsNullOrEmpty(efiBcdPath) && File.Exists(efiBcdPath))
            {
                bcdStorePath = efiBcdPath;
                isBcdAvailable = true;
                WriteDebug($"Found EFI BCD: {bcdStorePath}");
                return;
            }
            string fallbackPath = Path.Combine(offlineDrive, "Windows", "Boot", "BCD");
            if (File.Exists(fallbackPath))
            {
                bcdStorePath = fallbackPath;
                isBcdAvailable = true;
                WriteDebug($"Found fallback BCD: {bcdStorePath}");
                return;
            }
            isBcdAvailable = false;
            WriteDebug("No BCD store found.");
        }

        private string GetEfiBcdPathViaPowerShell()
        {
            try
            {
                string driveLetter = offlineDrive.TrimEnd(':');
                string script = $@"
$ErrorActionPreference = 'Stop'
$driveLetter = '{driveLetter}'
$disk = (Get-Partition -DriveLetter $driveLetter).DiskNumber
if (-not $disk) {{ throw 'Could not get disk number' }}
$efiPart = Get-Partition -DiskNumber $disk | Where-Object {{ $_.GptType -eq '{{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}}' }}
if (-not $efiPart) {{ throw 'EFI partition not found' }}
$vol = $efiPart | Get-Volume -ErrorAction SilentlyContinue
if ($vol -and $vol.UniqueId) {{ $vol.UniqueId }}
else {{
    $path = $efiPart | Select-Object -ExpandProperty AccessPaths | Where-Object {{ $_ -like '\\?\Volume*' }} | Select-Object -First 1
    if ($path) {{ $path }} else {{ throw 'No UniqueId or AccessPath' }}
}}";
                string uniqueId = RunPowerShellScript(script);
                if (string.IsNullOrEmpty(uniqueId)) return null;
                if (!uniqueId.StartsWith(@"\\?\")) uniqueId = @"\\?\" + uniqueId;
                return Path.Combine(uniqueId.TrimEnd('\\'), @"EFI\Microsoft\Boot\BCD");
            }
            catch (Exception ex)
            {
                WriteDebug($"PowerShell error: {ex.Message}");
                return null;
            }
        }

        private string RunPowerShellScript(string scriptContent)
        {
            string tempScript = Path.GetTempFileName() + ".ps1";
            try
            {
                File.WriteAllText(tempScript, scriptContent, Encoding.UTF8);
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempScript}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    string error = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (!string.IsNullOrEmpty(error)) WriteDebug($"PowerShell error: {error}");
                    return output?.Trim();
                }
            }
            finally { try { File.Delete(tempScript); } catch { } }
        }
        #endregion

        #region Core BCD Operations
        private string RunBcdEditCommand(string args)
        {
            using (Process p = new Process())
            {
                p.StartInfo.FileName = "bcdedit.exe";
                p.StartInfo.Arguments = args;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.Start();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0 && string.IsNullOrEmpty(output))
                    output = p.StandardError.ReadToEnd();
                WriteDebug($"bcdedit {args} -> exit {p.ExitCode}");
                return output;
            }
        }

        private List<BcdEntry> ParseBcdOutputFull(string output)
        {
            var entries = new List<BcdEntry>();
            var blocks = Regex.Split(output, @"(?=^[a-zA-Z0-9\s\-]+?\-+?\r?\n)", RegexOptions.Multiline);
            BcdEntry current = null;
            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block)) continue;
                var lines = block.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0) continue;
                string title = lines[0].Trim();
                if (title.EndsWith("---")) title = title.Substring(0, title.Length - 3).Trim();
                if (title.Contains("identifier") || lines.Skip(1).Any(l => l.TrimStart().StartsWith("identifier")))
                {
                    current = new BcdEntry { RawData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), Type = title };
                    entries.Add(current);
                }
                else if (current == null) continue;

                foreach (var line in lines.Skip(1))
                {
                    var match = Regex.Match(line, @"^(\S+)\s+(.+)$");
                    if (match.Success)
                    {
                        string key = match.Groups[1].Value.Trim();
                        string value = match.Groups[2].Value.Trim();
                        current.RawData[key] = value;
                        if (key.Equals("identifier", StringComparison.OrdinalIgnoreCase))
                            current.Identifier = value;
                    }
                }
                if (string.IsNullOrEmpty(current?.Identifier))
                    entries.Remove(current);
            }

            foreach (var entry in entries)
            {
                entry.FriendlyName = GetFriendlyName(entry.Identifier, entry.RawData);
                entry.Description = entry.RawData.ContainsKey("description") ? entry.RawData["description"] : "";
                entry.Device = entry.RawData.ContainsKey("device") ? entry.RawData["device"] : "";
                entry.OsDevice = entry.RawData.ContainsKey("osdevice") ? entry.RawData["osdevice"] : "";
            }
            return entries;
        }

        private string GetFriendlyName(string identifier, Dictionary<string, string> data)
        {
            var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["{bootmgr}"] = "Windows Boot Manager",
                ["{default}"] = "Default OS",
                ["{current}"] = "Current OS",
                ["{memdiag}"] = "Windows Memory Diagnostic",
                ["{ntldr}"] = "NTLDR (Legacy)",
                ["{resume}"] = "Resume from Hibernate"
            };
            if (known.ContainsKey(identifier)) return known[identifier];
            if (data.ContainsKey("description") && !string.IsNullOrEmpty(data["description"]))
                return data["description"];
            return identifier;
        }

        private void LoadBcdEntries()
        {
            string output = RunBcdEditCommand("/store \"" + bcdStorePath + "\" /enum all /v");
            bcdEntries = ParseBcdOutputFull(output);
            RefreshDataGridView();
        }

        private void RefreshDataGridView()
        {
            dgvEntries.DataSource = null;
            dgvEntries.AutoGenerateColumns = false;
            dgvEntries.Columns.Clear();

            dgvEntries.Columns.Add(new DataGridViewTextBoxColumn { Name = "Identifier", HeaderText = "Identifier", DataPropertyName = "Identifier", Width = 200 });
            dgvEntries.Columns.Add(new DataGridViewTextBoxColumn { Name = "FriendlyName", HeaderText = "Friendly Name", DataPropertyName = "FriendlyName", Width = 180 });
            dgvEntries.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Entry Type", DataPropertyName = "Type", Width = 150 });
            dgvEntries.Columns.Add(new DataGridViewTextBoxColumn { Name = "Description", HeaderText = "Description", DataPropertyName = "Description", Width = 200 });
            dgvEntries.Columns.Add(new DataGridViewTextBoxColumn { Name = "Device", HeaderText = "Device", DataPropertyName = "Device", Width = 250 });
            dgvEntries.Columns.Add(new DataGridViewTextBoxColumn { Name = "OsDevice", HeaderText = "OS Device", DataPropertyName = "OsDevice", Width = 250 });

            dgvEntries.DataSource = bcdEntries;
        }

        private void LoadBootTimeout()
        {
            string output = RunBcdEditCommand("/store \"" + bcdStorePath + "\" /enum {bootmgr}");
            Match match = Regex.Match(output, @"timeout\s+(\d+)", RegexOptions.IgnoreCase);
            if (match.Success)
                nudTimeout.Value = int.Parse(match.Groups[1].Value);
        }

        private void NudTimeout_ValueChanged(object sender, EventArgs e)
        {
            if (isBcdAvailable && File.Exists(bcdStorePath))
                RunBcdEditCommand($"/store \"{bcdStorePath}\" /set {{bootmgr}} timeout {(int)nudTimeout.Value}");
        }

        private void BackupBcd()
        {
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Title = "Save BCD Backup";
                sfd.Filter = "BCD files|*.bcd|All files|*.*";
                sfd.FileName = $"BCD_backup_{DateTime.Now:yyyyMMdd_HHmmss}.bcd";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    File.Copy(bcdStorePath, sfd.FileName, true);
                    MessageBox.Show($"Backup saved to {sfd.FileName}", "Backup Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
        #endregion

        #region Basic Button Handlers
        private void BtnBackup_Click(object sender, EventArgs e) => BackupBcd();
        private void BtnRefresh_Click(object sender, EventArgs e)
        {
            if (isBcdAvailable && File.Exists(bcdStorePath))
            {
                LoadBcdEntries();
                LoadBootTimeout();
                lblStatus.Text = $"Refreshed: {bcdStorePath}";
            }
        }
        private void BtnLoadBackup_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Loading a backup will overwrite the current BCD store.\nContinue?", "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "Select BCD Backup";
                ofd.Filter = "BCD files|*.bcd|All files|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    File.Copy(ofd.FileName, bcdStorePath, true);
                    LoadBcdEntries();
                    LoadBootTimeout();
                    MessageBox.Show("Backup restored.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
        #endregion

        #region Double-click Edit
        private void DgvEntries_DoubleClick(object sender, EventArgs e)
        {
            if (dgvEntries.CurrentRow == null) return;
            var entry = dgvEntries.CurrentRow.DataBoundItem as BcdEntry;
            if (entry != null)
                EditBcdEntryFull(entry);
        }

        private void EditBcdEntryFull(BcdEntry entry)
        {
            Form editForm = new Form();
            editForm.Text = $"Edit Entry: {entry.FriendlyName}";
            editForm.Size = new Size(600, 500);
            editForm.StartPosition = FormStartPosition.CenterParent;

            PropertyGrid propGrid = new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = entry.RawData };
            Button btnOk = new Button { Text = "Save", DialogResult = DialogResult.OK, Width = 80, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            Button btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            FlowLayoutPanel buttonPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Height = 40, Dock = DockStyle.Bottom };
            buttonPanel.Controls.Add(btnOk);
            buttonPanel.Controls.Add(btnCancel);
            editForm.Controls.Add(propGrid);
            editForm.Controls.Add(buttonPanel);

            if (editForm.ShowDialog() == DialogResult.OK)
            {
                foreach (var kv in entry.RawData)
                {
                    if (!string.IsNullOrEmpty(kv.Value))
                        RunBcdEditCommand($"/store \"{bcdStorePath}\" /set {{{entry.Identifier}}} {kv.Key} \"{kv.Value}\"");
                }
                LoadBcdEntries();
                MessageBox.Show("Entry updated.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        #endregion

        #region Repair BCD using bcdboot with automatic EFI mounting (mountvol + diskpart fallback)
        private void BtnRepairBcd_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("This will rebuild the BCD using bcdboot.\nA backup will be created first.\nContinue?", "Repair BCD", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            BackupBcd();

            string windowsDir = Path.Combine(offlineDrive, "Windows");
            if (!Directory.Exists(windowsDir))
            {
                MessageBox.Show($"Windows directory not found: {windowsDir}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string lang = GetWindowsLanguage(windowsDir);
            string sysPartDrive = GetAlreadyMountedEfiDrive();
            bool tempMounted = false;

            if (string.IsNullOrEmpty(sysPartDrive))
            {
                if (!MountEfiPartitionAutomatically(out sysPartDrive))
                {
                    sysPartDrive = Microsoft.VisualBasic.Interaction.InputBox(
                        "Could not auto-mount EFI partition.\nEnter the drive letter where the EFI/Boot folder is located (e.g., K:):",
                        "System Partition", "");
                    if (string.IsNullOrEmpty(sysPartDrive)) return;
                }
                else
                {
                    tempMounted = true;
                }
            }

            sysPartDrive = sysPartDrive.TrimEnd('\\');
            if (!sysPartDrive.EndsWith(":")) sysPartDrive += ":";

            if (!Directory.Exists(sysPartDrive))
            {
                MessageBox.Show($"Drive {sysPartDrive} does not exist.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (tempMounted) UnmountEfiPartition(sysPartDrive);
                return;
            }

            string firmware = "ALL";
            if (Directory.Exists(Path.Combine(sysPartDrive, "EFI")))
                firmware = "UEFI";
            else
                firmware = "BIOS";

            string args = $"\"{windowsDir}\" /l {lang} /s {sysPartDrive} /f {firmware}";
            var psi = new ProcessStartInfo("bcdboot.exe", args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd();
                string error = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode == 0)
                {
                    MessageBox.Show("BCD rebuilt successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    LoadBcdEntries();
                    LoadBootTimeout();
                }
                else
                {
                    MessageBox.Show($"bcdboot failed.\nCommand: bcdboot {args}\nError: {error}\nOutput: {output}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            if (tempMounted && !string.IsNullOrEmpty(sysPartDrive))
            {
                UnmountEfiPartition(sysPartDrive);
            }
        }

        private string GetWindowsLanguage(string windowsDir)
        {
            string softwareHive = Path.Combine(windowsDir, "System32", "config", "SOFTWARE");
            if (!File.Exists(softwareHive)) return "en-us";
            string tempKey = "BCDRepairTemp";
            RunCommand($"reg load HKLM\\{tempKey} \"{softwareHive}\"");
            string lang = RunCommand($"reg query HKLM\\{tempKey}\\Microsoft\\Windows\\CurrentVersion /v InstallLanguage");
            RunCommand($"reg unload HKLM\\{tempKey}");
            Match m = Regex.Match(lang, @"InstallLanguage\s+REG_SZ\s+(\w+)");
            return m.Success ? m.Groups[1].Value : "en-us";
        }

        private string RunCommand(string cmd)
        {
            using (Process p = new Process())
            {
                p.StartInfo.FileName = "cmd.exe";
                p.StartInfo.Arguments = "/c " + cmd;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                return p.StandardOutput.ReadToEnd();
            }
        }

        private string GetAlreadyMountedEfiDrive()
        {
            string efiPath = GetEfiBcdPathViaPowerShell();
            if (string.IsNullOrEmpty(efiPath)) return null;
            string volumeGuid = Path.GetPathRoot(efiPath)?.TrimEnd('\\');
            if (string.IsNullOrEmpty(volumeGuid)) return null;

            string mountVolOutput = RunCommand("mountvol");
            var lines = mountVolOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string currentGuid = null;
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith(@"\\?\"))
                {
                    currentGuid = trimmed;
                }
                else if (!string.IsNullOrEmpty(currentGuid) && trimmed.EndsWith(@"\"))
                {
                    if (trimmed.Length >= 2 && trimmed[1] == ':')
                    {
                        string driveLetter = trimmed.Substring(0, 2);
                        if (currentGuid.Equals(volumeGuid, StringComparison.OrdinalIgnoreCase))
                            return driveLetter;
                    }
                    currentGuid = null;
                }
            }
            return null;
        }

private bool MountEfiPartitionAutomatically(out string driveLetter)
{
    driveLetter = null;

    // Get the EFI partition number and disk number using PowerShell
    string script = $@"
$driveLetter = '{offlineDrive.TrimEnd(':')}'
$disk = (Get-Partition -DriveLetter $driveLetter).DiskNumber
$efiPart = Get-Partition -DiskNumber $disk | Where-Object {{ $_.GptType -eq '{{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}}' }}
if ($efiPart) {{
    Write-Output ""$disk|$($efiPart.PartitionNumber)""
}} else {{
    Write-Error ""EFI partition not found""
}}";
    string result = RunPowerShellScript(script);
    WriteDebug($"MountEfiPartitionAutomatically: PowerShell result = '{result}'");
    if (string.IsNullOrEmpty(result) || !result.Contains("|"))
    {
        WriteDebug("Could not determine EFI partition number");
        return false;
    }
    string[] parts = result.Split('|');
    string diskNumber = parts[0];
    string partitionNumber = parts[1];
    WriteDebug($"EFI partition: Disk {diskNumber}, Partition {partitionNumber}");

    // Get all used drive letters (including network, subst, etc.)
    var usedLetters = GetUsedDriveLetters();
    WriteDebug($"Used drive letters: {string.Join(",", usedLetters)}");

    // Try drive letters from Z down to D
    char candidate = 'Z';
    while (candidate >= 'D')
    {
        if (!usedLetters.Contains(candidate))
        {
            driveLetter = candidate + ":";
            WriteDebug($"Trying to assign drive letter {driveLetter}");

            // Create diskpart script to assign the letter
            string diskpartScript = $@"select disk {diskNumber}
select partition {partitionNumber}
assign letter={driveLetter.TrimEnd(':')}
exit";
            string scriptFile = Path.GetTempFileName() + ".txt";
            try
            {
                File.WriteAllText(scriptFile, diskpartScript);
                string diskpartOutput = RunCommand($"diskpart /s \"{scriptFile}\"");
                WriteDebug($"diskpart output: {diskpartOutput}");
                System.Threading.Thread.Sleep(500);
                // Verify mount
                string check = RunCommand($"mountvol {driveLetter} /L");
                if (!string.IsNullOrEmpty(check) && check.Contains(@"\\?\"))
                {
                    WriteDebug($"Successfully mounted EFI partition to {driveLetter}");
                    return true;
                }
                else
                {
                    WriteDebug($"Failed to verify mount for {driveLetter}, trying next letter");
                }
            }
            catch (Exception ex)
            {
                WriteDebug($"Exception: {ex.Message}");
            }
            finally
            {
                try { File.Delete(scriptFile); } catch { }
            }
        }
        candidate--;
    }

    WriteDebug("No free drive letter could be assigned.");
    return false;
}

private HashSet<char> GetUsedDriveLetters()
{
    var used = new HashSet<char>();
    // Get from mountvol (local drives)
    string mountVolOutput = RunCommand("mountvol");
    var matches = Regex.Matches(mountVolOutput, @"([A-Z]):\\");
    foreach (Match m in matches)
        used.Add(m.Groups[1].Value[0]);

    // Get from logical disks (including network, subst, etc.)
    string wmicOutput = RunCommand("wmic logicaldisk get deviceid");
    var lines = wmicOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    foreach (var line in lines)
    {
        var match = Regex.Match(line, @"([A-Z]):");
        if (match.Success)
            used.Add(match.Groups[1].Value[0]);
    }
    return used;
}

        private void UnmountEfiPartition(string driveLetter)
        {
            RunCommand($"mountvol {driveLetter} /d");
            WriteDebug($"Unmounted EFI partition from {driveLetter}");
        }
        #endregion

        private async void BCDManagementForm_Load(object sender, EventArgs e)
        {
            lblStatus.Text = "Locating BCD store...";
            try
            {
                await Task.Run(() => LocateBcdStore());
                if (isBcdAvailable)
                {
                    LoadBcdEntries();
                    LoadBootTimeout();
                    lblStatus.Text = $"BCD store: {bcdStorePath}";
                }
                else
                {
                    MessageBox.Show("Could not locate BCD store. BCD management unavailable.\n\nCheck " + debugLogPath + " for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to locate BCD: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
            }
        }
    }

    public class BcdEntry
    {
        public string Identifier { get; set; }
        public string FriendlyName { get; set; }
        public string Type { get; set; }
        public string Description { get; set; }
        public string Device { get; set; }
        public string OsDevice { get; set; }
        public Dictionary<string, string> RawData { get; set; } = new Dictionary<string, string>();
    }
}