using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace DimSim_Windows_Repair
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (!IsAdministrator())
            {
                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        UseShellExecute = true,
                        Verb = "runas",
                        FileName = Application.ExecutablePath
                    };
                    Process.Start(startInfo);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Unable to elevate to administrator: {ex.Message}", "Elevation Failed",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                Application.Exit();
                return;
            }

            Application.Run(new MainForm());
        }

        private static bool IsAdministrator()
        {
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
        }
    }
}