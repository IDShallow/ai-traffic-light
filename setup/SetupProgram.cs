using System;
using System.IO;
using System.Security.Principal;
using System.Windows.Forms;

namespace ClaudeTrafficLight.Setup
{
    internal static class SetupProgram
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                bool silent = Has(args, "--silent") || Has(args, "/S");
                bool uninstall = Has(args, "--uninstall");
                bool restart = Has(args, "--restart");
                string dir = ValueOf(args, "--dir");

                if (uninstall)
                {
                    DoUninstall(dir, silent);
                    return;
                }

                bool? desktop = null;
                if (Has(args, "--desktop")) desktop = true;
                if (Has(args, "--no-desktop")) desktop = false;

                bool? autoStart = null;
                if (Has(args, "--autostart")) autoStart = true;
                if (Has(args, "--no-autostart")) autoStart = false;

                InstallOptions o = new InstallOptions();
                o.Dir = dir;
                o.DesktopShortcut = desktop;
                o.AutoStart = autoStart;
                o.RunAfter = restart;
                o.Silent = silent;

                if (silent)
                {
                    if (NeedsElevation(dir) && !IsAdmin())
                    {
                        RelaunchElevated(args);
                        return;
                    }

                    try
                    {
                        Installer.Install(o, delegate(string s) { Log.Write("setup: " + s); });
                        Environment.ExitCode = 0;
                    }
                    catch (Exception ex)
                    {
                        Log.Write("setup failed: " + ex);
                        Environment.ExitCode = 1;
                    }
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try { Win32.SetProcessDPIAware(); } catch { }
                try
                {
                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                        Ui.Scale = g.DpiX / 96f;
                }
                catch { Ui.Scale = 1f; }
                if (Ui.Scale < 1f) Ui.Scale = 1f;
                Application.Run(new SetupForm());
            }
            catch (Exception ex)
            {
                try { Log.Write("setup fatal: " + ex); } catch { }
                if (!(Has(args, "--silent") || Has(args, "/S")))
                {
                    try
                    {
                        MessageBox.Show(ex.Message, "安装程序出错", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    catch { }
                }
                Environment.ExitCode = 1;
            }
        }

        private static void DoUninstall(string dir, bool silent)
        {
            if (!silent)
            {
                string msg = "确定要卸载 " + AppInfo.ProductName + " 吗？\n\n" +
                             "程序目录会被删除；\n设置与日志会保留在 %APPDATA%\\ClaudeTrafficLight。";
                if (MessageBox.Show(msg, "卸载 " + AppInfo.ProductName,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            }

            try
            {
                Installer.Uninstall(dir, silent, delegate(string s) { Log.Write("uninstall: " + s); });
                if (!silent)
                {
                    MessageBox.Show("卸载完成。", "卸载", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                Log.Write("uninstall failed: " + ex);
                if (!silent)
                    MessageBox.Show(ex.Message, "卸载失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Environment.ExitCode = 1;
            }
        }

        private static bool NeedsElevation(string dir)
        {
            string target = string.IsNullOrEmpty(dir) ? Installer.ExistingDir() : dir;
            if (string.IsNullOrEmpty(target)) target = Installer.DefaultDir();
            try
            {
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                return (!string.IsNullOrEmpty(pf) && target.StartsWith(pf, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(pf86) && target.StartsWith(pf86, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        private static bool IsAdmin()
        {
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private static void RelaunchElevated(string[] args)
        {
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                for (int i = 0; i < args.Length; i++)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append('"').Append(args[i].Replace("\"", "\\\"")).Append('"');
                }
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(
                    Application.ExecutablePath, sb.ToString());
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                Log.Write("elevate failed: " + ex.Message);
            }
        }

        private static bool Has(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string ValueOf(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                    return args[i].Substring(name.Length + 1).Trim('"');
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    return args[i + 1].Trim('"');
            }
            return "";
        }
    }
}
