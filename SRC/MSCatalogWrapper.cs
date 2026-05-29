#nullable disable

using System;
using System.IO;
using System.Threading.Tasks;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace DimSim_Windows_Repair
{
    public static class MSCatalogWrapper
    {
        // Use a fixed location that is easy to access
        private static readonly string TempDownloadFolder = @"C:\DimSimOfflineUpdates";
        private static bool _moduleChecked = false;

        public static async Task<string> DownloadUpdateAsync(string kbNumber, string destinationFolder = null)
        {
            string downloadDir = destinationFolder ?? TempDownloadFolder;
            if (!Directory.Exists(downloadDir))
                Directory.CreateDirectory(downloadDir);

            await EnsureModuleAvailable();

            string downloadedFile = await DownloadWithPowerShell(kbNumber, downloadDir);
            return downloadedFile;
        }

        private static async Task EnsureModuleAvailable()
        {
            if (_moduleChecked) return;

            using (PowerShell ps = PowerShell.Create())
            {
                ps.AddScript("Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force");
                await Task.Run(() => ps.Invoke());
                ps.Commands.Clear();

                ps.AddScript("Get-Module -ListAvailable -Name MSCatalogLTS");
                var modules = await Task.Run(() => ps.Invoke());

                if (modules.Count == 0)
                {
                    throw new Exception(
                        "MSCatalogLTS PowerShell module is not installed.\n\n" +
                        "Please install it as Administrator by following these steps:\n\n" +
                        "1. Open PowerShell as Administrator\n" +
                        "2. Run: Install-Module -Name MSCatalogLTS -Scope AllUsers -Force\n" +
                        "3. Run: Get-ChildItem 'C:\\Program Files\\WindowsPowerShell\\Modules\\MSCatalogLTS' -Recurse | Unblock-File\n\n" +
                        "After installation, restart this application.");
                }
                _moduleChecked = true;
            }
        }

        private static async Task<string> DownloadWithPowerShell(string kbNumber, string downloadDir)
        {
            // Use a temporary file to capture the output file path
            string tempResultFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + "_result.txt");
            string tempErrorFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + "_error.txt");
            
            var runspace = RunspaceFactory.CreateRunspace();
            runspace.Open();
            using (PowerShell ps = PowerShell.Create())
            {
                ps.Runspace = runspace;

                // Set execution policy for this process
                ps.AddScript("Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force");
                await Task.Run(() => ps.Invoke());
                ps.Commands.Clear();

                // Robust PowerShell script with proper error handling
                string script = $@"
                    try {{
                        Import-Module MSCatalogLTS -ErrorAction Stop
                        $update = Get-MSCatalogUpdate -Search '{kbNumber}' -ErrorAction Stop
                        if (-not $update) {{ throw 'No update found for {kbNumber}' }}
                        # Save the update (download if needed)
                        $update | Save-MSCatalogUpdate -Destination '{downloadDir}' -DownloadAll -ErrorAction Stop
                        # Wait a bit for file system
                        Start-Sleep -Seconds 2
                        # Find the most recent .msu or .cab file in the destination folder (case-insensitive)
                        $files = Get-ChildItem -Path '{downloadDir}' -Include '*.msu','*.cab' -Recurse -ErrorAction SilentlyContinue
                        if (-not $files) {{
                            throw 'No .msu or .cab files found in {downloadDir}'
                        }}
                        $latestFile = $files | Sort-Object LastWriteTime -Descending | Select-Object -First 1
                        $latestFile.FullName | Out-File '{tempResultFile}' -Encoding UTF8
                    }}
                    catch {{
                        $_ | Out-File '{tempErrorFile}' -Encoding UTF8
                        throw
                    }}
                ";

                ps.AddScript(script);
                await Task.Run(() => ps.Invoke());

                // Wait for file writes
                await Task.Delay(2000);

                // Check for errors
                if (ps.HadErrors)
                {
                    string errorMsg = "PowerShell errors: ";
                    foreach (ErrorRecord err in ps.Streams.Error)
                        errorMsg += err.Exception.Message + "; ";
                    
                    if (File.Exists(tempErrorFile))
                    {
                        errorMsg += "\nDetails: " + File.ReadAllText(tempErrorFile);
                    }
                    throw new Exception(errorMsg);
                }

                // Read the result
                if (!File.Exists(tempResultFile))
                    throw new Exception("Download completed but could not retrieve file path.");

                string downloadedFile = File.ReadAllText(tempResultFile).Trim();
                
                // Cleanup temp files
                try { File.Delete(tempResultFile); } catch { }
                try { File.Delete(tempErrorFile); } catch { }

                if (string.IsNullOrEmpty(downloadedFile) || !File.Exists(downloadedFile))
                    throw new Exception($"Download completed but file not found: {downloadedFile}");

                return downloadedFile;
            }
        }
    }
}