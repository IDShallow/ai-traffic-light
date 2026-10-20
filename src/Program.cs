using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ClaudeTrafficLight
{
    internal static class Program
    {
        private const string MutexName = "Local\\ClaudeTrafficLight_SingleInstance";
        private const string ShowEventName = "Local\\ClaudeTrafficLight_Show";
        private const string QuitEventName = "Local\\ClaudeTrafficLight_Quit";
        private static Mutex _mutex;

        [STAThread]
        private static void Main(string[] args)
        {
            if (HasArg(args, "--hook"))
            {
                RunHook();
                return;
            }

            if (HasArg(args, "--quit"))
            {
                SignalQuit();
                return;
            }

            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                SignalExisting();
                return;
            }

            try { Win32.SetProcessDPIAware(); }
            catch { }
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    Ui.Scale = g.DpiX / 96f;
            }
            catch { Ui.Scale = 1f; }
            if (Ui.Scale < 1f) Ui.Scale = 1f;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            AppSettings settings = AppSettings.Load();
            bool runAtLogin = AutoStart.IsEnabled();
            if (runAtLogin) settings.AutoStart = true;
            bool startHidden = settings.StartMinimized || HasArg(args, "--minimized");
            Log.Write("=== start pid=" + Process.GetCurrentProcess().Id + " exe=" + Paths.ExePath +
                      " scale=" + Ui.Scale.ToString("0.00") + " hidden=" + startHidden +
                      " runAtLogin=" + runAtLogin + " autoStart=" + settings.AutoStart);

            Monitor monitor = new Monitor(settings);
            monitor.Start();

            MainForm form = new MainForm(settings, monitor, startHidden);

            EventWaitHandle showEvent = null;
            EventWaitHandle quitEvent = null;
            Thread waiter = null;
            try
            {
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
                WaitHandle[] handles = new WaitHandle[] { showEvent, quitEvent };

                waiter = new Thread(delegate()
                {
                    while (!form.IsDisposed)
                    {
                        try
                        {
                            int idx = WaitHandle.WaitAny(handles, 500);
                            if (form.IsDisposed) return;
                            if (idx == 0)
                                form.BeginInvoke((MethodInvoker)delegate { form.ShowMain(); });
                            else if (idx == 1)
                                form.BeginInvoke((MethodInvoker)delegate { form.ExitFromOutside(); });
                        }
                        catch { return; }
                    }
                });
                waiter.IsBackground = true;
                waiter.Start();
            }
            catch { }

            try
            {
                Application.Run(form);
            }
            finally
            {
                monitor.Dispose();
                if (showEvent != null) showEvent.Close();
                if (quitEvent != null) quitEvent.Close();
            }
        }

        private static bool HasArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static void SignalExisting()
        {
            try
            {
                EventWaitHandle ev;
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out ev))
                {
                    using (ev) ev.Set();
                    return;
                }
            }
            catch { }

            try
            {
                Process[] ps = Process.GetProcessesByName("ClaudeTrafficLight");
                for (int i = 0; i < ps.Length; i++)
                {
                    if (ps[i].MainWindowHandle != IntPtr.Zero)
                    {
                        Win32.ForceForeground(ps[i].MainWindowHandle);
                        return;
                    }
                }
            }
            catch { }
        }

        /// <summary>让已经在跑的实例退出（安装/升级前用）。</summary>
        private static void SignalQuit()
        {
            try
            {
                EventWaitHandle ev;
                if (EventWaitHandle.TryOpenExisting(QuitEventName, out ev))
                {
                    using (ev) ev.Set();
                }
            }
            catch { }
        }

        /// <summary>hook 模式：读 stdin 的 JSON，落一个事件文件，立刻退出。绝不能拖慢 Claude。</summary>
        private static void RunHook()
        {
            try
            {
                string text;
                using (Stream stdin = Console.OpenStandardInput())
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] buf = new byte[8192];
                    int n;
                    while ((n = stdin.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                    text = Encoding.UTF8.GetString(ms.ToArray());
                }
                if (string.IsNullOrWhiteSpace(text)) return;

                Dictionary<string, object> d = Json.ParseObject(text);
                if (d == null) return;
                if (Json.Str(d, "session_id").Length == 0) return;

                Directory.CreateDirectory(Paths.EventsDir);
                PruneEvents();

                string name = DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture) + "-" +
                              Guid.NewGuid().ToString("N").Substring(0, 8) + ".json";
                File.WriteAllText(Path.Combine(Paths.EventsDir, name), text, new UTF8Encoding(false));
            }
            catch
            {
                // hook 永远不能把 Claude 的主流程拖死
            }
        }

        private static void PruneEvents()
        {
            try
            {
                string[] files = Directory.GetFiles(Paths.EventsDir, "*.json");
                if (files.Length < 40) return;
                DateTime cutoff = DateTime.UtcNow.AddHours(-2);
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(files[i]) < cutoff) File.Delete(files[i]);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
