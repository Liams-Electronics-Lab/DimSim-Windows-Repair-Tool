using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DimSim_Windows_Repair
{
    public static class CapabilityManager
    {
        public class CapabilityInfo
        {
            public string CapabilityName { get; set; }
            public string DisplayName { get; set; }
            public string State { get; set; }
        }

        /// <summary>
        /// Scans the offline Windows image for all capabilities (Features on Demand).
        /// </summary>
        public static async Task<List<CapabilityInfo>> ScanCapabilitiesAsync(string offlineDrive)
        {
            var capabilities = new List<CapabilityInfo>();
            string args = $"/Image:{offlineDrive}\\ /Get-Capabilities /English";
            string output = await RunDismGetOutputAsync(args);

            // Match multi-line blocks: "Capability Identity : ... State : ..."
            Regex regex = new Regex(@"Capability Identity : (.+?)\s*State : (.+)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var matches = regex.Matches(output);
            foreach (Match match in matches)
            {
                if (match.Groups.Count >= 3)
                {
                    string identity = match.Groups[1].Value.Trim();
                    string state = match.Groups[2].Value.Trim();
                    string display = GetDisplayName(identity);
                    capabilities.Add(new CapabilityInfo { CapabilityName = identity, DisplayName = display, State = state });
                }
            }
            return capabilities;
        }

        private static string GetDisplayName(string identity)
        {
            // Friendly name mapping for common capabilities
            if (identity.StartsWith("Language")) return "Language Pack";
            if (identity.Contains("OpenSSH")) return "OpenSSH Client";
            if (identity.Contains("Printing")) return "Printing Services";
            if (identity.Contains("MediaPlayback")) return "Media Playback";
            if (identity.Contains("XPS")) return "XPS Services";
            if (identity.Contains("InternetExplorer")) return "Internet Explorer 11";
            if (identity.Contains("WindowsMediaPlayer")) return "Windows Media Player";
            // Fallback: use the first part of the identity
            var parts = identity.Split('~');
            return parts.Length > 0 ? parts[0] : identity;
        }

        /// <summary>
        /// Uninstalls a capability from the offline image.
        /// </summary>
        /// <param name="offlineDrive">Drive letter (e.g., "D:")</param>
        /// <param name="capabilityName">Full capability identity</param>
        /// <param name="logCallback">Action to receive real‑time output</param>
        /// <returns>True if successful (exit code 0 or 3010)</returns>
        public static async Task<bool> UninstallCapabilityAsync(string offlineDrive, string capabilityName, Action<string> logCallback)
        {
            string args = $"/Image:{offlineDrive}\\ /Remove-Capability /CapabilityName:{capabilityName}";
            int exitCode = await RunDismCommandAsync(args, logCallback);
            return exitCode == 0 || exitCode == 3010;
        }

        /// <summary>
        /// Installs a capability from a source path (e.g., a mounted FoD ISO).
        /// </summary>
        public static async Task<bool> InstallCapabilityAsync(string offlineDrive, string capabilityName, string sourcePath, Action<string> logCallback)
        {
            string args = $"/Image:{offlineDrive}\\ /Add-Capability /CapabilityName:{capabilityName} /Source:{sourcePath} /LimitAccess";
            int exitCode = await RunDismCommandAsync(args, logCallback);
            return exitCode == 0 || exitCode == 3010;
        }

        private static async Task<string> RunDismGetOutputAsync(string arguments)
        {
            return await Task.Run(() =>
            {
                using (var p = new Process())
                {
                    p.StartInfo.FileName = "dism.exe";
                    p.StartInfo.Arguments = arguments;
                    p.StartInfo.CreateNoWindow = true;
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.RedirectStandardError = true;
                    p.Start();
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    return output;
                }
            });
        }

        private static async Task<int> RunDismCommandAsync(string arguments, Action<string> logCallback)
        {
            return await Task.Run(() =>
            {
                using (var p = new Process())
                {
                    p.StartInfo.FileName = "dism.exe";
                    p.StartInfo.Arguments = arguments;
                    p.StartInfo.CreateNoWindow = true;
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.RedirectStandardError = true;
                    p.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) logCallback?.Invoke(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) logCallback?.Invoke("[ERROR] " + e.Data); };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    p.WaitForExit();
                    return p.ExitCode;
                }
            });
        }
    }
}