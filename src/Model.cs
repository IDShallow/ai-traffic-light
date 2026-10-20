using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace ClaudeTrafficLight
{
    internal enum SessState
    {
        Offline = 0,
        Working = 1,
        NeedsInput = 2,
        Done = 3,
        Error = 4
    }

    internal sealed class SessionInfo
    {
        public string Provider = "claude";   // claude | codex | dsh
        public string SessionId = "";
        public int Pid;
        public string Cwd = "";
        public string Title = "";
        public string ProjectName = "";
        public string Entrypoint = "";
        public string Kind = "";
        public string StatusRaw = "";
        public string WaitingFor = "";
        public DateTime StatusAt = DateTime.MinValue;

        public bool Alive;
        public SessState State = SessState.Offline;
        public bool Unread;
        public DateTime StateSince = DateTime.MinValue;

        public string LastPrompt = "";
        public string LastAssistant = "";
        public string LastTool = "";
        public string Model = "";
        public string ErrorText = "";
        public int TodoDone;
        public int TodoTotal;
        public DateTime LastActivity = DateTime.MinValue;
        public DateTime FirstSeen = DateTime.MinValue;

        public string TranscriptPath = "";
        public string HookEvent = "";
        public string HookMessage = "";
        public DateTime HookAt = DateTime.MinValue;

        public string DisplayTitle()
        {
            if (!string.IsNullOrEmpty(Title)) return Title;

            string p;
            if (Provider == "codex") p = "Codex";
            else if (Provider == "dsh") p = "DSH";
            else
            {
                p = ProjectName;
                if (string.IsNullOrEmpty(p)) p = "Claude";
            }

            if (SessionId.Length >= 8) return p + " · " + SessionId.Substring(0, 8);
            return p;
        }

        public string StateText()
        {
            switch (State)
            {
                case SessState.Working: return "工作中";
                case SessState.NeedsInput: return "等待你回答";
                case SessState.Done: return Unread ? "已完成（未读）" : "已完成";
                case SessState.Error: return "出错";
                default: return "已结束";
            }
        }
    }

    internal sealed class AppSettings
    {
        public int[] MainPos;
        public int[] MainSize;
        public int[] OverviewPos;
        public bool TopMost = true;
        public bool OverviewVisible;
        public bool Notify = true;
        public bool Sound = true;
        public bool NotifyNeeds = true;
        public bool NotifyDone = true;
        public bool NotifyError = true;
        public bool AutoStart;
        public bool ShowOffline;
        public bool SortByState = true;
        public bool HooksEnabled;
        public bool PrivacyMode;
        public bool MonitorClaude = true;
        public bool MonitorCodex = true;
        public bool MonitorDsh = true;
        public double Opacity = 1.0;
        public bool StartMinimized;

        private static readonly JavaScriptSerializer Ser = new JavaScriptSerializer();

        public static AppSettings Load()
        {
            AppSettings s = new AppSettings();
            try
            {
                if (!File.Exists(Paths.SettingsFile)) return s;
                string text = File.ReadAllText(Paths.SettingsFile);
                if (string.IsNullOrEmpty(text.Trim())) return s;
                object raw = Ser.DeserializeObject(text);
                Dictionary<string, object> d = raw as System.Collections.Generic.Dictionary<string, object>;
                if (d == null) return s;

                s.MainPos = Ints(d, "mainPos");
                s.MainSize = Ints(d, "mainSize");
                s.OverviewPos = Ints(d, "overviewPos");
                s.TopMost = Json.Bool(d, "topMost", s.TopMost);
                s.OverviewVisible = Json.Bool(d, "overviewVisible", s.OverviewVisible);
                s.Notify = Json.Bool(d, "notify", s.Notify);
                s.Sound = Json.Bool(d, "sound", s.Sound);
                s.NotifyNeeds = Json.Bool(d, "notifyNeeds", s.NotifyNeeds);
                s.NotifyDone = Json.Bool(d, "notifyDone", s.NotifyDone);
                s.NotifyError = Json.Bool(d, "notifyError", s.NotifyError);
                s.AutoStart = Json.Bool(d, "autoStart", s.AutoStart);
                s.ShowOffline = Json.Bool(d, "showOffline", s.ShowOffline);
                s.SortByState = Json.Bool(d, "sortByState", s.SortByState);
                s.HooksEnabled = Json.Bool(d, "hooksEnabled", s.HooksEnabled);
                s.PrivacyMode = Json.Bool(d, "privacyMode", s.PrivacyMode);
                s.MonitorClaude = Json.Bool(d, "monitorClaude", s.MonitorClaude);
                s.MonitorCodex = Json.Bool(d, "monitorCodex", s.MonitorCodex);
                s.MonitorDsh = Json.Bool(d, "monitorDsh", s.MonitorDsh);
                s.Opacity = Number(d, "opacity", s.Opacity);
                s.StartMinimized = Json.Bool(d, "startMinimized", s.StartMinimized);
            }
            catch
            {
                // 配置坏了就用默认值，不影响运行
            }
            return s;
        }

        private static double Number(Dictionary<string, object> d, string key, double def)
        {
            if (d == null || !d.ContainsKey(key)) return def;
            try { return Convert.ToDouble(d[key], CultureInfo.InvariantCulture); }
            catch { return def; }
        }

        private static int[] Ints(Dictionary<string, object> d, string key)
        {
            object[] a = Json.List(d, key);
            if (a == null || a.Length < 2) return null;
            try
            {
                return new int[]
                {
                    Convert.ToInt32(a[0], CultureInfo.InvariantCulture),
                    Convert.ToInt32(a[1], CultureInfo.InvariantCulture)
                };
            }
            catch { return null; }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Paths.AppData);
                System.Collections.Generic.Dictionary<string, object> d =
                    new System.Collections.Generic.Dictionary<string, object>();
                d["mainPos"] = MainPos;
                d["mainSize"] = MainSize;
                d["overviewPos"] = OverviewPos;
                d["topMost"] = TopMost;
                d["overviewVisible"] = OverviewVisible;
                d["notify"] = Notify;
                d["sound"] = Sound;
                d["notifyNeeds"] = NotifyNeeds;
                d["notifyDone"] = NotifyDone;
                d["notifyError"] = NotifyError;
                d["autoStart"] = AutoStart;
                d["showOffline"] = ShowOffline;
                d["sortByState"] = SortByState;
                d["hooksEnabled"] = HooksEnabled;
                d["privacyMode"] = PrivacyMode;
                d["monitorClaude"] = MonitorClaude;
                d["monitorCodex"] = MonitorCodex;
                d["monitorDsh"] = MonitorDsh;
                d["opacity"] = Opacity;
                d["startMinimized"] = StartMinimized;
                File.WriteAllText(Paths.SettingsFile, Json.Pretty(d), new System.Text.UTF8Encoding(false));
            }
            catch
            {
                // 存不下就算了，下次还能跑
            }
        }
    }

    internal static class Paths
    {
        public static string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        public static string ClaudeDir = Path.Combine(Home, ".claude");
        public static string SessionsDir = Path.Combine(ClaudeDir, "sessions");
        public static string ProjectsDir = Path.Combine(ClaudeDir, "projects");
        public static string AppData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeTrafficLight");
        public static string LocalData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeTrafficLight");
        public static string EventsDir = Path.Combine(LocalData, "events");
        public static string SettingsFile = Path.Combine(AppData, "settings.json");
        public static string ClaudeSettingsFile = Path.Combine(ClaudeDir, "settings.json");
        public static string ExePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
    }

    /// <summary>开机自启：写 HKCU\...\Run。安装包也写这份，应用启动时按它校准设置。</summary>
    internal static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsEnabled()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k =
                    Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    if (k == null) return false;
                    string v = k.GetValue(AppInfo.RunValueName) as string;
                    if (string.IsNullOrEmpty(v)) return false;
                    return v.IndexOf(Paths.ExePath, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }

        public static void Set(bool on)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k =
                    Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on)
                        k.SetValue(AppInfo.RunValueName, "\"" + Paths.ExePath + "\" --minimized");
                    else
                        k.DeleteValue(AppInfo.RunValueName, false);
                }
            }
            catch { }
        }
    }
}
