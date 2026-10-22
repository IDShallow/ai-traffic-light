using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ClaudeTrafficLight
{
    /// <summary>
    /// DeepSeek Harness（DSH）会话：
    /// ~/.dsh/storages/session_projcache/sessions/session-*.json
    ///
    /// 每个文件里有 { record: { identity, rows } }：
    ///   identity.cwd       工作目录
    ///   rows.title.val     会话标题
    ///   rows.turnOutline.val.turns[]  每轮的 prompt / response（取最后一轮）
    ///   rows.inbox.val.next-turn / next-step 非空  ⇒ 等你输入
    ///   rows.sessionStats.val.openStep / pendingCalls
    ///   rows.turnBoundary.val.openTurnStartSeq       ⇒ 正在跑
    ///   rows.goal.val.failure                        ⇒ 出错
    /// </summary>
    internal static class DshProvider
    {
        private sealed class Parsed
        {
            public DateTime FileTime;
            public long Length;
            public string SessionId = "";
            public string Title = "";
            public string Cwd = "";
            public string LastPrompt = "";
            public string LastAssistant = "";
            public string ErrorText = "";
            public string WaitingFor = "";
            public DateTime LastPromptAt = DateTime.MinValue;
            public SessState State = SessState.Done;
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Parsed> Cache =
            new Dictionary<string, Parsed>(StringComparer.OrdinalIgnoreCase);
        private static int _pid;
        private static DateTime _pidAt = DateTime.MinValue;

        public static List<SessionInfo> Scan(AppSettings settings, DateTime now)
        {
            List<SessionInfo> list = new List<SessionInfo>();
            if (settings == null || !settings.MonitorDsh) return list;

            string dir = Path.Combine(Paths.Home, ".dsh");
            string cacheDir = Path.Combine(dir, "storages", "session_projcache", "sessions");
            if (!Directory.Exists(cacheDir)) return list;

            int pid;
            bool running = IsRunning(out pid);

            string[] files;
            try { files = Directory.GetFiles(cacheDir, "*.json"); }
            catch { return list; }

            List<KeyValuePair<DateTime, string>> recent = new List<KeyValuePair<DateTime, string>>();
            for (int i = 0; i < files.Length; i++)
            {
                DateTime mt;
                try { mt = File.GetLastWriteTimeUtc(files[i]); }
                catch { continue; }
                if ((now - mt).TotalDays > 7) continue;
                recent.Add(new KeyValuePair<DateTime, string>(mt, files[i]));
            }
            recent.Sort(delegate(KeyValuePair<DateTime, string> a, KeyValuePair<DateTime, string> b)
            {
                return b.Key.CompareTo(a.Key);
            });

            int count = 0;
            for (int i = 0; i < recent.Count && count < 10; i++)
            {
                string f = recent[i].Value;
                DateTime mt = recent[i].Key;
                long len;
                try { len = new FileInfo(f).Length; } catch { continue; }

                Parsed p;
                lock (Sync)
                {
                    if (!Cache.TryGetValue(f, out p) || p.FileTime != mt || p.Length != len)
                    {
                        p = Parse(f);
                        p.FileTime = mt;
                        p.Length = len;
                        Cache[f] = p;
                    }
                }

                double ageMin = (now - mt).TotalMinutes;
                bool alive = running || ageMin < 2;
                if (!alive)
                {
                    if (!settings.ShowOffline || ageMin > 30) continue;
                }

                SessionInfo s = new SessionInfo();
                s.Provider = "dsh";
                s.SessionId = p.SessionId;
                s.Title = p.Title;
                s.Cwd = p.Cwd;
                s.ProjectName = Ui.BaseName(p.Cwd);
                s.Entrypoint = "dsh-desktop";
                s.Pid = pid;
                s.Alive = alive;
                s.State = alive ? p.State : SessState.Offline;
                s.WaitingFor = p.WaitingFor;
                s.LastPrompt = p.LastPrompt;
                s.LastAssistant = p.LastAssistant;
                s.ErrorText = p.ErrorText;
                s.LastActivity = mt;
                s.LastPromptAt = p.LastPromptAt;
                s.StateSince = mt;
                s.TranscriptPath = f;
                list.Add(s);
                count++;
            }

            return list;
        }

        private static bool IsRunning(out int pid)
        {
            pid = 0;
            if ((DateTime.UtcNow - _pidAt).TotalSeconds < 3)
            {
                pid = _pid;
                return pid != 0;
            }
            _pidAt = DateTime.UtcNow;

            string[] names = new string[] { "DeepSeek Harness", "DeepSeekHarness", "dsh" };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    Process[] ps = Process.GetProcessesByName(names[i]);
                    if (ps.Length > 0)
                    {
                        pid = ps[0].Id;
                        break;
                    }
                }
                catch { }
            }

            _pid = pid;
            return pid != 0;
        }

        private static Parsed Parse(string path)
        {
            Parsed p = new Parsed();
            p.SessionId = Path.GetFileNameWithoutExtension(path);
            if (p.SessionId.StartsWith("session-", StringComparison.OrdinalIgnoreCase))
                p.SessionId = p.SessionId.Substring(8);

            try
            {
                Dictionary<string, object> root = Json.ParseObject(File.ReadAllText(path));
                if (root == null) return p;

                Dictionary<string, object> rec = Json.Map(root, "record");
                if (rec == null) return p;

                Dictionary<string, object> identity = Json.Map(rec, "identity");
                if (identity != null) p.Cwd = Json.Str(identity, "cwd");

                Dictionary<string, object> rows = Json.Map(rec, "rows");
                if (rows == null) return p;

                p.Title = RowString(rows, "title");

                Dictionary<string, object> meta = RowMap(rows, "sessionListMetadata");
                if (meta != null)
                {
                    long lastPromptMs = Json.Long(meta, "lastPromptAt", 0);
                    if (lastPromptMs > 0)
                        p.LastPromptAt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                            .AddMilliseconds(lastPromptMs);
                }

                Dictionary<string, object> outline = RowMap(rows, "turnOutline");
                object[] turns = Json.List(outline, "turns");
                if (turns != null && turns.Length > 0)
                {
                    Dictionary<string, object> last =
                        turns[turns.Length - 1] as Dictionary<string, object>;
                    if (last != null)
                    {
                        string prompt = Json.Str(last, "prompt");
                        string response = Json.Str(last, "response");
                        if (prompt.Length > 0) p.LastPrompt = Cap(prompt, 2000);
                        if (response.Length > 0) p.LastAssistant = Cap(response, 4000);
                    }
                }

                Dictionary<string, object> inbox = RowMap(rows, "inbox");
                bool needs = NonEmptyList(inbox, "next-turn") || NonEmptyList(inbox, "next-step");

                Dictionary<string, object> stats = RowMap(rows, "sessionStats");
                bool working = NonNull(stats, "openStep");
                Dictionary<string, object> pending = Json.Map(stats, "pendingCalls");
                if (pending != null && pending.Count > 0) working = true;

                Dictionary<string, object> boundary = RowMap(rows, "turnBoundary");
                if (NonNull(boundary, "openTurnStartSeq")) working = true;

                Dictionary<string, object> retry = RowMap(rows, "llmRetry");
                if (retry != null && retry.Count > 0) working = true;

                Dictionary<string, object> goal = RowMap(rows, "goal");
                Dictionary<string, object> failure = Json.Map(goal, "failure");
                bool failed = failure != null && failure.Count > 0;

                if (failed)
                {
                    p.State = SessState.Error;
                    p.ErrorText = Cap(FailureText(failure), 2000);
                    if (p.ErrorText.Length == 0) p.ErrorText = "DeepSeek Harness 报告了一个失败";
                }
                else if (needs)
                {
                    p.State = SessState.NeedsInput;
                    p.WaitingFor = "有未处理的输入";
                }
                else if (working)
                {
                    p.State = SessState.Working;
                }
                else
                {
                    p.State = SessState.Done;
                }
            }
            catch { }

            return p;
        }

        private static string FailureText(Dictionary<string, object> failure)
        {
            string s = Json.Str(failure, "message");
            if (s.Length > 0) return s;
            s = Json.Str(failure, "reason");
            if (s.Length > 0) return s;
            s = Json.Str(failure, "error");
            if (s.Length > 0) return s;
            return Json.Stringify(failure);
        }

        private static object RowVal(Dictionary<string, object> rows, string key)
        {
            if (rows == null) return null;
            Dictionary<string, object> w = Json.Map(rows, key);
            if (w == null) return null;
            return w.ContainsKey("val") ? w["val"] : null;
        }

        private static Dictionary<string, object> RowMap(Dictionary<string, object> rows, string key)
        {
            return RowVal(rows, key) as Dictionary<string, object>;
        }

        private static string RowString(Dictionary<string, object> rows, string key)
        {
            object v = RowVal(rows, key);
            return v as string ?? "";
        }

        private static bool NonNull(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key)) return false;
            object v = d[key];
            if (v == null) return false;
            string s = v as string;
            if (s != null) return s.Length > 0;
            return true;
        }

        private static bool NonEmptyList(Dictionary<string, object> d, string key)
        {
            object[] arr = Json.List(d, key);
            return arr != null && arr.Length > 0;
        }

        private static string Cap(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max);
        }
    }
}
