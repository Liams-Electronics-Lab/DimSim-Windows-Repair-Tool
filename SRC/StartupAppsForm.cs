using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DimSim_Windows_Repair
{
    public class StartupAppsForm : Form
    {
        private readonly string _offlineDrive;
        private DataGridView dgvStartup;
        private RichTextBox rtbConsole;
        private ProgressBar pbProgress;
        private Label lblStatus;
        private Button btnRefresh;
        private CancellationTokenSource _cts;

        public StartupAppsForm(string offlineDrive)
        {
            _offlineDrive = offlineDrive;
            InitializeComponent();
            this.Load += async (s, e) => await LoadStartupEntriesAsync();
            this.FormClosing += (s, e) => _cts?.Cancel();
        }

        private void InitializeComponent()
        {
            this.Text = $"Startup Apps Manager - {_offlineDrive}";
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
            btnRefresh.Click += async (s, e) => await LoadStartupEntriesAsync();
            topPanel.Controls.Add(btnRefresh);

            dgvStartup = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false
            };
            dgvStartup.CellClick += DgvStartup_CellClick;

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
                Style = ProgressBarStyle.Marquee
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

            mainLayout.Controls.Add(topPanel, 0, 0);
            mainLayout.Controls.Add(dgvStartup, 0, 1);
            mainLayout.Controls.Add(bottomPanel, 0, 2);

            this.Controls.Add(mainLayout);
        }

        private async Task LoadStartupEntriesAsync()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                await Task.Delay(200);
            }
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            btnRefresh.Enabled = false;
            pbProgress.Visible = true;
            lblStatus.Visible = true;
            lblStatus.Text = "Scanning startup entries...";
            rtbConsole.Clear();
            AppendToConsole("Starting startup entry scan...\r\n");

            try
            {
                var entries = await Task.Run(() => ScanStartupEntries(token), token);
                if (token.IsCancellationRequested) return;

                DataTable table = new DataTable();
                table.Columns.Add("User", typeof(string));
                table.Columns.Add("AppName", typeof(string));
                table.Columns.Add("Path", typeof(string));
                table.Columns.Add("Status", typeof(string));
                table.Columns.Add("RegPath", typeof(string));
                table.Columns.Add("IsMachine", typeof(bool));
                table.Columns.Add("UserHiveName", typeof(string));
                table.Columns.Add("UserNtuserPath", typeof(string));

                foreach (var entry in entries)
                {
                    DataRow row = table.NewRow();
                    row["User"] = entry.User;
                    row["AppName"] = entry.AppName;
                    row["Path"] = entry.Path;
                    row["Status"] = entry.Status;
                    row["RegPath"] = entry.RegistryKeyPath;
                    row["IsMachine"] = entry.IsMachine;
                    row["UserHiveName"] = entry.UserHiveName ?? "";
                    row["UserNtuserPath"] = entry.UserNtuserPath ?? "";
                    table.Rows.Add(row);
                }

                DataView view = table.DefaultView;
                view.Sort = "User ASC";
                dgvStartup.DataSource = view;
                dgvStartup.Columns["RegPath"].Visible = false;
                dgvStartup.Columns["IsMachine"].Visible = false;
                dgvStartup.Columns["UserHiveName"].Visible = false;
                dgvStartup.Columns["UserNtuserPath"].Visible = false;

                if (!dgvStartup.Columns.Contains("Enable"))
                {
                    DataGridViewButtonColumn btnEnable = new DataGridViewButtonColumn
                    {
                        Name = "Enable",
                        HeaderText = "Action",
                        Text = "Enable",
                        UseColumnTextForButtonValue = true,
                        Width = 80
                    };
                    DataGridViewButtonColumn btnDisable = new DataGridViewButtonColumn
                    {
                        Name = "Disable",
                        HeaderText = "",
                        Text = "Disable",
                        UseColumnTextForButtonValue = true,
                        Width = 80
                    };
                    dgvStartup.Columns.Add(btnEnable);
                    dgvStartup.Columns.Add(btnDisable);
                }

                AppendToConsole($"Scan completed. Found {entries.Count} startup entries.\r\n");
            }
            catch (OperationCanceledException)
            {
                AppendToConsole("Scan cancelled.\r\n");
            }
            catch (Exception ex)
            {
                AppendToConsole($"ERROR: {ex.Message}\r\n");
                MessageBox.Show($"Failed to load startup entries: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnRefresh.Enabled = true;
                pbProgress.Visible = false;
                lblStatus.Visible = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private class StartupEntry
        {
            public string User { get; set; }
            public string AppName { get; set; }
            public string Path { get; set; }
            public string Status { get; set; }
            public string RegistryKeyPath { get; set; }
            public bool IsMachine { get; set; }
            public string UserHiveName { get; set; }
            public string UserNtuserPath { get; set; }
        }

        private List<StartupEntry> ScanStartupEntries(CancellationToken token)
        {
            List<StartupEntry> entries = new List<StartupEntry>();
            string softwareHive = Path.Combine(_offlineDrive, "Windows", "System32", "config", "SOFTWARE");
            if (!File.Exists(softwareHive))
                throw new Exception("SOFTWARE hive not found. Invalid Windows installation?");

            // Load machine SOFTWARE hive
            AppendToConsole("Loading machine SOFTWARE hive...\r\n");
            RunRegCommand($"load HKLM\\Offline_Software \"{softwareHive}\"");
            try
            {
                using (RegistryKey machineRun = Registry.LocalMachine.OpenSubKey("Offline_Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
                {
                    if (machineRun != null)
                    {
                        foreach (string valueName in machineRun.GetValueNames())
                        {
                            string value = machineRun.GetValue(valueName) as string;
                            if (!string.IsNullOrEmpty(value))
                            {
                                string status = GetStartupStatus("Offline_Software", valueName, isMachine: true);
                                entries.Add(new StartupEntry
                                {
                                    User = "All Users (Machine)",
                                    AppName = valueName,
                                    Path = value,
                                    Status = status,
                                    RegistryKeyPath = $"Offline_Software\\Microsoft\\Windows\\CurrentVersion\\Run",
                                    IsMachine = true,
                                    UserHiveName = null,
                                    UserNtuserPath = null
                                });
                            }
                        }
                    }
                }
            }
            finally
            {
                AppendToConsole("Unloading machine SOFTWARE hive...\r\n");
                UnloadHive("HKLM\\Offline_Software");
            }

            // Scan each user profile
            string usersPath = Path.Combine(_offlineDrive, "Users");
            if (Directory.Exists(usersPath))
            {
                var userFolders = Directory.GetDirectories(usersPath);
                foreach (string userFolder in userFolders)
                {
                    token.ThrowIfCancellationRequested();
                    string userName = Path.GetFileName(userFolder);
                    if (userName.Equals("Public", StringComparison.OrdinalIgnoreCase) ||
                        userName.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                        userName.Equals("Default User", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string ntuserPath = Path.Combine(userFolder, "NTUSER.DAT");
                    if (File.Exists(ntuserPath))
                    {
                        string hiveName = $"OfflineUser_{userName}";
                        AppendToConsole($"Loading user hive for {userName}...\r\n");
                        RunRegCommand($"load HKLM\\{hiveName} \"{ntuserPath}\"");
                        try
                        {
                            using (RegistryKey userRun = Registry.LocalMachine.OpenSubKey($"{hiveName}\\Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
                            {
                                if (userRun != null)
                                {
                                    foreach (string valueName in userRun.GetValueNames())
                                    {
                                        string value = userRun.GetValue(valueName) as string;
                                        if (!string.IsNullOrEmpty(value))
                                        {
                                            string status = GetStartupStatus(hiveName, valueName, isMachine: false);
                                            entries.Add(new StartupEntry
                                            {
                                                User = userName,
                                                AppName = valueName,
                                                Path = value,
                                                Status = status,
                                                RegistryKeyPath = $"{hiveName}\\Software\\Microsoft\\Windows\\CurrentVersion\\Run",
                                                IsMachine = false,
                                                UserHiveName = hiveName,
                                                UserNtuserPath = ntuserPath
                                            });
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            AppendToConsole($"Error reading user hive for {userName}: {ex.Message}\r\n");
                        }
                        finally
                        {
                            AppendToConsole($"Unloading user hive for {userName}...\r\n");
                            UnloadHive($"HKLM\\{hiveName}");
                        }
                    }
                }
            }
            return entries;
        }

        private string GetStartupStatus(string hivePath, string appName, bool isMachine)
        {
            string approvedKeyPath;
            if (isMachine)
                approvedKeyPath = $"{hivePath}\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run";
            else
                approvedKeyPath = $"{hivePath}\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run";

            using (RegistryKey approvedKey = Registry.LocalMachine.OpenSubKey(approvedKeyPath))
            {
                if (approvedKey != null)
                {
                    byte[] value = approvedKey.GetValue(appName) as byte[];
                    if (value != null && value.Length >= 1 && value[0] == 0x03)
                        return "Disabled";
                }
            }
            return "Enabled";
        }

        private async void DgvStartup_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            string columnName = dgvStartup.Columns[e.ColumnIndex].Name;
            if (columnName != "Enable" && columnName != "Disable") return;

            DataGridViewRow row = dgvStartup.Rows[e.RowIndex];
            if (row.DataBoundItem == null) return;

            DataRowView rowView = (DataRowView)row.DataBoundItem;
            string appName = rowView["AppName"].ToString();
            string regPath = rowView["RegPath"].ToString();
            bool isMachine = (bool)rowView["IsMachine"];
            string userHiveName = rowView["UserHiveName"].ToString();
            string userNtuserPath = rowView["UserNtuserPath"].ToString();

            btnRefresh.Enabled = false;
            try
            {
                if (columnName == "Enable")
                    await SetStartupState(appName, regPath, isMachine, userHiveName, userNtuserPath, enable: true);
                else
                    await SetStartupState(appName, regPath, isMachine, userHiveName, userNtuserPath, enable: false);
            }
            finally
            {
                btnRefresh.Enabled = true;
                await LoadStartupEntriesAsync();
            }
        }

        private async Task SetStartupState(string appName, string regPath, bool isMachine, string userHiveName, string userNtuserPath, bool enable)
        {
            AppendToConsole($"{(enable ? "Enabling" : "Disabling")} startup entry: {appName} for {(isMachine ? "machine" : "user " + userHiveName)}...\r\n");

            await Task.Run(() =>
            {
                string hiveRoot = null;
                string approvedKeyPath = null;
                bool loadedHive = false;

                try
                {
                    if (isMachine)
                    {
                        string softwareHive = Path.Combine(_offlineDrive, "Windows", "System32", "config", "SOFTWARE");
                        RunRegCommand($"load HKLM\\Offline_Software \"{softwareHive}\"");
                        loadedHive = true;
                        hiveRoot = "HKLM\\Offline_Software";
                        approvedKeyPath = $"{hiveRoot}\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run";
                    }
                    else
                    {
                        RunRegCommand($"load HKLM\\{userHiveName} \"{userNtuserPath}\"");
                        loadedHive = true;
                        hiveRoot = $"HKLM\\{userHiveName}";
                        approvedKeyPath = $"{hiveRoot}\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run";
                    }

                    if (enable)
                    {
                        // Remove the restriction key to enable (no "reg" prefix)
                        RunRegCommand($"delete \"{approvedKeyPath}\" /v \"{appName}\" /f", true);
                    }
                    else
                    {
                        // Ensure parent key exists (no "reg" prefix)
                        RunRegCommand($"add \"{approvedKeyPath}\" /f", true);
                        // Write disable bytes
                        byte[] disableBytes = new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                        string hex = BitConverter.ToString(disableBytes).Replace("-", "");
                        RunRegCommand($"add \"{approvedKeyPath}\" /v \"{appName}\" /t REG_BINARY /d {hex} /f", true);
                    }
                }
                finally
                {
                    if (loadedHive)
                    {
                        if (isMachine)
                            UnloadHive("HKLM\\Offline_Software");
                        else
                            UnloadHive($"HKLM\\{userHiveName}");
                    }
                }
            });

            AppendToConsole($"Operation completed.\r\n");
        }

        private void RunRegCommand(string command, bool silent = false)
        {
            using (Process p = new Process())
            {
                p.StartInfo.FileName = "reg.exe";
                p.StartInfo.Arguments = command;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.Start();
                string output = p.StandardOutput.ReadToEnd();
                string error = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (!silent && !string.IsNullOrEmpty(output))
                    AppendToConsole(output);
                if (!string.IsNullOrEmpty(error))
                    AppendToConsole("[ERROR] " + error + "\r\n");
                if (p.ExitCode != 0 && !silent)
                    AppendToConsole($"Command exited with code {p.ExitCode}\r\n");
            }
        }

        private void UnloadHive(string fullHivePath)
        {
            Environment.CurrentDirectory = "C:\\";
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Thread.Sleep(200);
            RunRegCommand($"unload {fullHivePath}", true);
        }

        private void AppendToConsole(string text)
        {
            if (rtbConsole.InvokeRequired)
            {
                rtbConsole.Invoke(new Action<string>(AppendToConsole), text);
                return;
            }
            rtbConsole.AppendText(text);
            rtbConsole.ScrollToCaret();
        }
    }
}