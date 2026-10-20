using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClaudeTrafficLight.Setup
{
    internal sealed class InstallOptions
    {
        public string Dir = "";
        public bool? DesktopShortcut;   // null = 保持现状
        public bool? AutoStart;         // null = 保持现状
        public bool RunAfter;
        public bool Silent;
    }

    internal static class Installer
    {
        private const string RegKey = @"Software\ClaudeTrafficLight";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ClaudeTrafficLight";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static string DefaultDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", AppInfo.ProductId);
        }

        public static string ExistingDir()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RegKey))
                {
                    if (k == null) return null;
                    string d = k.GetValue("InstallDir") as string;
                    if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) return d;
                }
            }
            catch { }
            return null;
        }

        public static string ExistingVersion()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RegKey))
                {
                    if (k == null) return "";
                    return (k.GetValue("Version") as string) ?? "";
                }
            }
            catch { return ""; }
        }

        public static bool AutoStartEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    if (k == null) return false;
                    string v = k.GetValue(AppInfo.RunValueName) as string;
                    return !string.IsNullOrEmpty(v);
                }
            }
            catch { return false; }
        }

        public static string DesktopLnk()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                AppInfo.ProductName + ".lnk");
        }

        public static string StartMenuLnk()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                AppInfo.ProductName + ".lnk");
        }

        public static bool DesktopShortcutExists()
        {
            try { return File.Exists(DesktopLnk()); }
            catch { return false; }
        }

        // ---------- 安装 ----------

        public static void Install(InstallOptions o, Action<string> status)
        {
            bool upgrading = ExistingDir() != null;
            string dir = o.Dir;
            if (string.IsNullOrEmpty(dir)) dir = ExistingDir();
            if (string.IsNullOrEmpty(dir)) dir = DefaultDir();
            dir = Path.GetFullPath(dir);

            status("准备目录…");
            Directory.CreateDirectory(dir);

            string target = Path.Combine(dir, AppInfo.ExeName);

            status("停止正在运行的程序…");
            StopRunning(target);

            status("写入程序文件…");
            ExtractPayload(target);

            string uninst = Path.Combine(dir, AppInfo.UninstallName);
            string self = Application.ExecutablePath;
            if (!string.Equals(self, uninst, StringComparison.OrdinalIgnoreCase))
            {
                status("写入卸载程序…");
                File.Copy(self, uninst, true);
            }

            status("创建快捷方式…");
            bool desktop = o.DesktopShortcut.HasValue
                ? o.DesktopShortcut.Value
                : (upgrading && DesktopShortcutExists());
            CreateShortcut(StartMenuLnk(), target, dir);
            if (desktop) CreateShortcut(DesktopLnk(), target, dir);
            else DeleteQuiet(DesktopLnk());

            status("写入注册表…");
            WriteRegistry(dir);

            if (o.AutoStart.HasValue) SetAutoStart(o.AutoStart.Value, target);

            status("完成");
            Log.Write("install ok dir=" + dir + " version=" + AppInfo.Version +
                      " upgrade=" + upgrading + " desktop=" + desktop);

            if (o.RunAfter)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(target)
                    {
                        WorkingDirectory = dir,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        private static void ExtractPayload(string target)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Payload"))
            {
                if (s == null) throw new Exception("安装包内部缺少程序文件（Payload）。");
                byte[] data = new byte[s.Length];
                int read = 0;
                while (read < data.Length)
                {
                    int n = s.Read(data, read, data.Length - read);
                    if (n <= 0) break;
                    read += n;
                }

                Exception last = null;
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    try
                    {
                        using (FileStream fs = new FileStream(target, FileMode.Create, FileAccess.Write,
                                   FileShare.None))
                        {
                            fs.Write(data, 0, read);
                            fs.Flush(true);
                        }
                        return;
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                        Thread.Sleep(250);
                    }
                }
                throw new Exception("写入程序文件失败（可能程序还在运行）：" + (last == null ? "" : last.Message));
            }
        }

        public static void StopRunning(string exePath)
        {
            try
            {
                if (File.Exists(exePath))
                {
                    Process.Start(new ProcessStartInfo(exePath, "--quit")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
            }
            catch { }

            Thread.Sleep(1200);

            try
            {
                Process[] ps = Process.GetProcessesByName(
                    Path.GetFileNameWithoutExtension(AppInfo.ExeName));
                for (int i = 0; i < ps.Length; i++)
                {
                    try
                    {
                        string path = ps[i].MainModule.FileName;
                        if (string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase))
                        {
                            ps[i].Kill();
                            ps[i].WaitForExit(3000);
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // 兜底：同一产品的其它副本也停掉 —— 否则单实例锁会让"安装完成后运行"只是唤起旧副本
            try
            {
                Process[] ps = Process.GetProcessesByName(
                    Path.GetFileNameWithoutExtension(AppInfo.ExeName));
                for (int i = 0; i < ps.Length; i++)
                {
                    try
                    {
                        if (!ps[i].HasExited)
                        {
                            ps[i].Kill();
                            ps[i].WaitForExit(3000);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void WriteRegistry(string dir)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RegKey))
            {
                if (k != null)
                {
                    k.SetValue("InstallDir", dir);
                    k.SetValue("Version", AppInfo.Version);
                }
            }

            string exe = Path.Combine(dir, AppInfo.ExeName);
            string uninst = Path.Combine(dir, AppInfo.UninstallName);

            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                if (k == null) return;
                k.SetValue("DisplayName", AppInfo.ProductName);
                k.SetValue("DisplayVersion", AppInfo.Version);
                k.SetValue("Publisher", "IDShallow");
                k.SetValue("InstallLocation", dir);
                k.SetValue("DisplayIcon", exe);
                k.SetValue("UninstallString", "\"" + uninst + "\" --uninstall");
                k.SetValue("QuietUninstallString", "\"" + uninst + "\" --uninstall --silent");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        public static void SetAutoStart(bool on, string exePath)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on && !string.IsNullOrEmpty(exePath))
                        k.SetValue(AppInfo.RunValueName, "\"" + exePath + "\" --minimized");
                    else
                        k.DeleteValue(AppInfo.RunValueName, false);
                }
            }
            catch { }
        }

        private static void CreateShortcut(string lnk, string target, string workingDir)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return;
                dynamic shell = Activator.CreateInstance(t);
                dynamic sc = shell.CreateShortcut(lnk);
                sc.TargetPath = target;
                sc.WorkingDirectory = workingDir;
                sc.IconLocation = target + ",0";
                sc.Description = AppInfo.ProductName;
                sc.Save();
            }
            catch { }
        }

        // ---------- 卸载 ----------

        public static void Uninstall(string dir, bool silent, Action<string> status)
        {
            if (string.IsNullOrEmpty(dir)) dir = ExistingDir();
            if (string.IsNullOrEmpty(dir)) dir = DefaultDir();

            status("停止程序…");
            StopRunning(Path.Combine(dir, AppInfo.ExeName));

            status("移除 Claude 钩子…");
            try
            {
                string err;
                HooksSetup.Uninstall(out err);
            }
            catch { }

            status("移除开机自启…");
            SetAutoStart(false, null);

            status("删除快捷方式…");
            DeleteQuiet(DesktopLnk());
            DeleteQuiet(StartMenuLnk());

            status("清理注册表…");
            try { Registry.CurrentUser.DeleteSubKeyTree(RegKey, false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }

            status("删除文件…");
            try { DeleteQuiet(Path.Combine(dir, AppInfo.ExeName)); } catch { }

            // 卸载程序自己就在目录里：交给一个等待 2 秒的 cmd 来删目录
            try
            {
                if (Directory.Exists(dir))
                {
                    string cmd = "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + dir + "\"";
                    Process.Start(new ProcessStartInfo("cmd.exe", cmd)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
            }
            catch { }

            Log.Write("uninstall ok dir=" + dir);
        }

        private static void DeleteQuiet(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }
    }
}
