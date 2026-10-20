using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace ClaudeTrafficLight
{
    /// <summary>
    /// Codex CLI 会话：~/.codex/sessions/&lt;年&gt;/&lt;月&gt;/&lt;日&gt;/rollout-*.jsonl
    /// 标题来自 ~/.codex/session_index.jsonl 的 thread_name。
    /// 状态取 roll-out 里最后一个事件：task_started / task_complete /
    /// exec_approval_request / turn_aborted / error。
    /// 说明：Codex 可能把旧 rollout 压缩成 .jsonl.zst；压缩件不解析（只监控活/近期的明文件）。
    /// </summary>
    internal static class CodexProvider
    {
        private sealed class Parsed
        {
            public DateTime FileTime;
            public long Length;
            public string SessionId = "";
            public string Cwd = "";
            public string Model = "";
            public string LastPrompt = "";
            public string LastAssistant = "";
            public string LastTool = "";
            public string ErrorText = "";
            public string WaitingFor = "";
            public SessState State = SessState.Done;
            public DateTime LastEntry = DateTime.MinValue;
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Parsed> Cache =
            new Dictionary<string, Parsed>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> Titles =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static DateTime _titlesAt = DateTime.MinValue;
        private static int _pid;
        private static DateTime _pidAt = DateTime.MinValue;
        private static string[] _filesCache;
        private static DateTime _filesAt = DateTime.MinValue;

        public static List<SessionInfo> Scan(AppSettings settings, DateTime now)
        {
            List<SessionInfo> list = new List<SessionInfo>();
            if (settings == null || !settings.MonitorCodex) return list;

            string dir = Path.Combine(Paths.Home, ".codex");
            string sessions = Path.Combine(dir, "sessions");
            if (!Directory.Exists(sessions)) return list;

            int pid;
            bool running = IsRunning(out pid);
            LoadTitles(dir);

            string[] files;
            if (_filesCache == null || (now - _filesAt).TotalSeconds > 10)
            {
                try { _filesCache = Directory.GetFiles(sessions, "rollout-*.jsonl", SearchOption.AllDirectories); }
                catch { return list; }
                _filesAt = now;
            }
            files = _filesCache;

            List<KeyValuePair<DateTime, string>> recent = new List<KeyValuePair<DateTime, string>>();
            for (int i = 0; i < files.Length; i++)
            {
                DateTime mt;
                try { mt = File.GetLastWriteTimeUtc(files[i]); }
                catch { continue; }
                if ((now - mt).TotalHours > 48) continue;
                recent.Add(new KeyValuePair<DateTime, string>(mt, files[i]));
            }
            recent.Sort(delegate(KeyValuePair<DateTime, string> a, KeyValuePair<DateTime, string> b)
            {
                return b.Key.CompareTo(a.Key);
            });

            int count = 0;
            for (int i = 0; i < recent.Count && count < 8; i++)
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
                bool alive = (running && ageMin < 120) || ageMin < 2;
                if (!alive)
                {
                    if (!settings.ShowOffline || ageMin > 30) continue;
                }

                SessionInfo s = new SessionInfo();
                s.Provider = "codex";
                // 解析只读文件尾部，老会话的 session_meta 在开头；文件名里一定有 ID
                string sid = p.SessionId.Length > 0 ? p.SessionId : GuessId(f);
                s.SessionId = sid;
                string title;
                if (sid.Length > 0 && Titles.TryGetValue(sid, out title))
                    s.Title = title;
                s.Cwd = p.Cwd;
                s.ProjectName = Ui.BaseName(p.Cwd);
                s.Entrypoint = "codex-cli";
                s.Pid = pid;
                s.Alive = alive;
                s.State = alive ? p.State : SessState.Offline;
                s.WaitingFor = p.WaitingFor;
                s.LastPrompt = p.LastPrompt;
                s.LastAssistant = p.LastAssistant;
                s.LastTool = p.LastTool;
                s.Model = p.Model;
                s.ErrorText = p.ErrorText;
                s.LastActivity = p.LastEntry > mt ? p.LastEntry : mt;
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
            try
            {
                Process[] ps = Process.GetProcessesByName("codex");
                if (ps.Length > 0) pid = ps[0].Id;
            }
            catch { }
            _pid = pid;
            return pid != 0;
        }

        private static void LoadTitles(string dir)
        {
            if ((DateTime.UtcNow - _titlesAt).TotalSeconds < 5) return;
            _titlesAt = DateTime.UtcNow;

            lock (Sync) Titles.Clear();

            string[] candidates = new string[]
            {
                Path.Combine(dir, "session_index.jsonl"),
                Path.Combine(dir, "sessions", "session_index.jsonl")
            };

            for (int c = 0; c < candidates.Length; c++)
            {
                try
                {
                    if (!File.Exists(candidates[c])) continue;
                    string[] lines = File.ReadAllLines(candidates[c]);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].Length == 0) continue;
                        Dictionary<string, object> d = Json.ParseObject(lines[i]);
                        if (d == null) continue;
                        string id = Json.Str(d, "id");
                        if (id.Length == 0) id = Json.Str(d, "session_id");
                        string name = Json.Str(d, "thread_name");
                        if (name.Length == 0) name = Json.Str(d, "title");
                        if (id.Length > 0 && name.Length > 0)
                        {
                            lock (Sync) Titles[id] = name;
                        }
                    }
                }
                catch { }
            }
        }

        private static Parsed Parse(string path)
        {
            Parsed p = new Parsed();
            string text = ReadTail(path, 256 * 1024);
            string[] lines = text.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0) continue;
                if (line[line.Length - 1] == '\r') line = line.Substring(0, line.Length - 1);
                if (line.Length == 0 || line.Length > 1024 * 1024) continue;

                Dictionary<string, object> o = Json.ParseObject(line);
                if (o == null) continue;

                string type = Json.Str(o, "type");
                DateTime ts = ParseTs(Json.Str(o, "timestamp"));
                if (ts > p.LastEntry) p.LastEntry = ts;

                Dictionary<string, object> payload = Json.Map(o, "payload");
                if (payload == null) continue;

                if (type == "session_meta")
                {
                    string id = Json.Str(payload, "id");
                    if (id.Length > 0) p.SessionId = id;
                    string cwd = Json.Str(payload, "cwd");
                    if (cwd.Length > 0) p.Cwd = cwd;
                }
                else if (type == "turn_context")
                {
                    string cwd = Json.Str(payload, "cwd");
                    if (cwd.Length > 0) p.Cwd = cwd;
                    string model = Json.Str(payload, "model");
                    if (model.Length > 0) p.Model = model;
                }
                else if (type == "event_msg")
                {
                    string et = Json.Str(payload, "type");
                    if (et == "task_started" || et == "exec_command_begin" || et == "exec_command_end" ||
                        et == "patch_apply_begin" || et == "patch_apply_end" ||
                        et == "mcp_tool_call_begin" || et == "mcp_tool_call_end" ||
                        et == "web_search_begin" || et == "web_search_end" ||
                        et == "agent_reasoning")
                    {
                        p.State = SessState.Working;
                        p.WaitingFor = "";
                    }
                    else if (et == "task_complete")
                    {
                        p.State = SessState.Done;
                        p.WaitingFor = "";
                        string m = Json.Str(payload, "last_agent_message");
                        if (m.Length > 0) p.LastAssistant = Cap(m, 4000);
                    }
                    else if (et == "turn_aborted")
                    {
                        p.State = SessState.Done;
                        p.WaitingFor = "已中止";
                    }
                    else if (et == "exec_approval_request")
                    {
                        p.State = SessState.NeedsInput;
                        p.WaitingFor = "等待批准命令";
                        if (p.LastTool.Length == 0) p.LastTool = "shell";
                    }
                    else if (et == "error" || et == "stream_error")
                    {
                        p.State = SessState.Error;
                        p.ErrorText = Cap(Json.Str(payload, "message"), 2000);
                    }
                    else if (et == "agent_message")
                    {
                        string m = Json.Str(payload, "message");
                        if (m.Length > 0) p.LastAssistant = Cap(m, 4000);
                        // 批准之后模型继续说话 ⇒ 已经恢复工作
                        if (p.State == SessState.NeedsInput) { p.State = SessState.Working; p.WaitingFor = ""; }
                    }
                    else if (et == "user_message")
                    {
                        string m = Json.Str(payload, "message");
                        if (m.Length > 0) p.LastPrompt = Cap(m, 2000);
                        if (p.State == SessState.NeedsInput) { p.State = SessState.Working; p.WaitingFor = ""; }
                    }
                }
                else if (type == "response_item")
                {
                    // 批准之后出现新的条目（工具输出等）⇒ 已经恢复工作
                    if (p.State == SessState.NeedsInput) { p.State = SessState.Working; p.WaitingFor = ""; }

                    string pt = Json.Str(payload, "type");
                    if (pt == "message")
                    {
                        string role = Json.Str(payload, "role");
                        string text2 = ExtractText(payload);
                        if (text2.Length > 0)
                        {
                            if (role == "assistant") p.LastAssistant = Cap(text2, 4000);
                            else if (role == "user") p.LastPrompt = Cap(text2, 2000);
                        }
                    }
                    else if (pt == "function_call")
                    {
                        string name = Json.Str(payload, "name");
                        if (name.Length > 0) p.LastTool = name;
                    }
                }
            }

            return p;
        }

        private static string ExtractText(Dictionary<string, object> payload)
        {
            object content = payload.ContainsKey("content") ? payload["content"] : null;
            string s = content as string;
            if (s != null) return s;

            object[] blocks = content as object[];
            if (blocks == null) return "";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < blocks.Length; i++)
            {
                Dictionary<string, object> b = blocks[i] as Dictionary<string, object>;
                if (b == null) continue;
                string t = Json.Str(b, "text");
                if (t.Length > 0)
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(t);
                }
            }
            return sb.ToString();
        }

        private static string ReadTail(string path, int maxBytes)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete))
                {
                    long len = fs.Length;
                    long start = Math.Max(0, len - maxBytes);
                    fs.Seek(start, SeekOrigin.Begin);
                    int n = (int)(len - start);
                    byte[] buf = new byte[n];
                    int read = 0;
                    while (read < n)
                    {
                        int k = fs.Read(buf, read, n - read);
                        if (k <= 0) break;
                        read += k;
                    }
                    string text = Encoding.UTF8.GetString(buf, 0, read);
                    if (start > 0)
                    {
                        int nl = text.IndexOf('\n');
                        if (nl >= 0) text = text.Substring(nl + 1);
                    }
                    return text;
                }
            }
            catch { return ""; }
        }

        private static string GuessId(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.Length >= 36)
            {
                string tail = name.Substring(name.Length - 36);
                int dashes = 0;
                for (int i = 0; i < tail.Length; i++) if (tail[i] == '-') dashes++;
                if (dashes == 4) return tail;
            }
            return name;
        }

        private static string Cap(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max);
        }

        private static DateTime ParseTs(string s)
        {
            if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
            DateTime dt;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out dt))
                return dt;
            return DateTime.MinValue;
        }
    }
}
