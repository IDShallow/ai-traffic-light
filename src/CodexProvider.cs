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
            // 对话中调用 request_user_input[_async] 后，这一轮会以 task_complete 结束，
            // 但实际是在等用户回答；要一直保持 NeedsInput 直到下一轮开始。
            public bool AskedUser;
            public string PendingSyncCallId = "";
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
        private static List<SessionInfo> _dbCache;
        private static DateTime _dbAt = DateTime.MinValue;

        public static List<SessionInfo> Scan(AppSettings settings, DateTime now)
        {
            List<SessionInfo> list = new List<SessionInfo>();
            if (settings == null || !settings.MonitorCodex) return list;

            string dir = Path.Combine(Paths.Home, ".codex");
            if (!Directory.Exists(dir)) return list;

            int pid;
            bool running = IsRunning(out pid);

            // 新版 Codex 桌面端（paginated 历史）不再追加 rollout，
            // 实时数据和状态在 state_*.sqlite / thread_history_*.sqlite 里
            List<SessionInfo> db;
            if (_dbCache != null && (now - _dbAt).TotalSeconds < 1)
            {
                db = CloneAll(_dbCache);
            }
            else
            {
                db = ScanDatabase(dir, settings, now, running, pid);
                _dbAt = now;
                _dbCache = db == null ? null : CloneAll(db);
            }
            List<SessionInfo> rolls = ScanRollouts(dir, settings, now, running, pid, null);
            HashSet<string> rollIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rolls.Count; i++) rollIds.Add(rolls[i].SessionId);

            if (db != null)
            {
                for (int i = 0; i < db.Count; i++)
                {
                    SessionInfo d = db[i];
                    if (rollIds.Contains(d.SessionId))
                    {
                        // 有 rollout：状态/消息以 rollout 为准，只把会话库里的标题与目录补进去
                        for (int j = 0; j < rolls.Count; j++)
                        {
                            if (!string.Equals(rolls[j].SessionId, d.SessionId, StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (!string.IsNullOrEmpty(d.Title)) rolls[j].Title = d.Title;
                            if (string.IsNullOrEmpty(rolls[j].Cwd) && !string.IsNullOrEmpty(d.Cwd))
                            {
                                rolls[j].Cwd = d.Cwd;
                                rolls[j].ProjectName = d.ProjectName;
                            }
                            break;
                        }
                        continue;
                    }
                    list.Add(d);
                }
            }
            list.AddRange(rolls);

            if (list.Count > 8)
            {
                list.Sort(delegate(SessionInfo a, SessionInfo b)
                {
                    return b.LastActivity.CompareTo(a.LastActivity);
                });
                list.RemoveRange(8, list.Count - 8);
            }
            return list;
        }

        private static List<SessionInfo> ScanRollouts(string dir, AppSettings settings, DateTime now,
            bool running, int pid, HashSet<string> skip)
        {
            List<SessionInfo> list = new List<SessionInfo>();
            string sessions = Path.Combine(dir, "sessions");
            if (!Directory.Exists(sessions)) return list;

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
                if (sid.Length == 0 || (skip != null && skip.Contains(sid))) continue;
                s.SessionId = sid;
                string title;
                if (Titles.TryGetValue(sid, out title))
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

        /// <summary>从 Codex 桌面端的 SQLite 会话库读会话（paginated 模式不写 rollout）。</summary>
        private static List<SessionInfo> ScanDatabase(string dir, AppSettings settings, DateTime now,
            bool running, int pid)
        {
            string statePath = FindDb(dir, "state_");
            if (statePath == null) return null;

            List<object[]> rows;
            using (SqliteDb db = SqliteDb.OpenReadOnly(statePath))
            {
                if (db == null) return null;
                rows = db.Query(
                    "select id, coalesce(title,''), coalesce(name,''), coalesce(first_user_message,''), " +
                    "coalesce(cwd,''), coalesce(rollout_path,''), coalesce(updated_at_ms,0), coalesce(updated_at,0) " +
                    "from threads where archived = 0 order by coalesce(updated_at_ms, updated_at*1000) desc limit 24");
            }

            List<SessionInfo> list = new List<SessionInfo>();
            for (int i = 0; i < rows.Count && list.Count < 8; i++)
            {
                object[] r = rows[i];
                string id = SqliteDb.Str(r, 0);
                long updMs = SqliteDb.Long(r, 6);
                if (updMs <= 0)
                {
                    // 老库只有秒级 updated_at
                    long sec = SqliteDb.Long(r, 7);
                    if (sec > 0) updMs = sec * 1000L;
                }
                if (id.Length == 0 || updMs <= 0) continue;

                DateTime updated = FromMs(updMs);
                if ((now - updated).TotalHours > 48) continue;

                double ageMin = (now - updated).TotalMinutes;
                bool alive = ageMin < 2 || (running && ageMin < 120);
                if (!alive && (!settings.ShowOffline || ageMin > 30)) continue;

                string title = SqliteDb.Str(r, 1);
                if (title.Length == 0) title = SqliteDb.Str(r, 2);
                if (title.Length == 0) title = SqliteDb.Str(r, 3);

                string cwd = SqliteDb.Str(r, 4);
                if (cwd.StartsWith("\\\\?\\")) cwd = cwd.Substring(4);

                SessionInfo s = new SessionInfo();
                s.Provider = "codex";
                s.SessionId = id;
                s.Title = Cap(Ui.Shorten(title, 200), 200);
                s.Cwd = cwd;
                s.ProjectName = Ui.BaseName(cwd);
                s.Entrypoint = "codex-desktop";
                s.Pid = pid;
                s.Alive = alive;
                s.State = alive ? SessState.Done : SessState.Offline;
                s.LastActivity = updated;
                s.StateSince = updated;
                s.TranscriptPath = SqliteDb.Str(r, 5);
                list.Add(s);
            }

            string histPath = FindDb(dir, "thread_history_");
            if (histPath == null || list.Count == 0) return list;

            using (SqliteDb hist = SqliteDb.OpenReadOnly(histPath))
            {
                if (hist == null) return list;
                for (int i = 0; i < list.Count; i++)
                {
                    SessionInfo s = list[i];
                    string id = s.SessionId;
                    string qid = id.Replace("'", "''");

                    List<object[]> turns = hist.Query(
                        "select coalesce(status,''), coalesce(started_at,0) from thread_turns " +
                        "where thread_id='" + qid + "' order by rollout_ordinal desc limit 1");
                    if (turns.Count > 0)
                    {
                        string waiting;
                        SessState st = MapTurnState(SqliteDb.Str(turns[0], 0), out waiting);
                        s.State = s.Alive ? st : SessState.Offline;
                        s.WaitingFor = waiting;
                        long started = SqliteDb.Long(turns[0], 1);
                        if (started > 0) s.StateSince = FromMs(started * 1000L);
                    }

                    List<object[]> items = hist.Query(
                        "select item_type, item_json from thread_items where thread_id='" + qid + "' " +
                        "and item_type in ('userMessage','agentMessage') and length(item_json) <= 65536 " +
                        "order by rollout_ordinal desc limit 8");
                    bool hasPrompt = false, hasReply = false;
                    for (int k = 0; k < items.Count; k++)
                    {
                        string type = SqliteDb.Str(items[k], 0);
                        if (type == "userMessage" && !hasPrompt)
                        {
                            string t = UserMessageText(SqliteDb.Str(items[k], 1));
                            if (t.Length > 0)
                            {
                                s.LastPrompt = Cap(t, 2000);
                                hasPrompt = true;
                            }
                        }
                        else if (type == "agentMessage" && !hasReply)
                        {
                            string t = AgentMessageText(SqliteDb.Str(items[k], 1));
                            if (t.Length > 0)
                            {
                                s.LastAssistant = Cap(t, 4000);
                                hasReply = true;
                            }
                        }
                    }
                }
            }
            return list;
        }

        private static List<SessionInfo> CloneAll(List<SessionInfo> src)
        {
            List<SessionInfo> list = new List<SessionInfo>(src.Count);
            for (int i = 0; i < src.Count; i++) list.Add(CloneInfo(src[i]));
            return list;
        }

        private static SessionInfo CloneInfo(SessionInfo a)
        {
            SessionInfo b = new SessionInfo();
            b.Provider = a.Provider;
            b.SessionId = a.SessionId;
            b.Pid = a.Pid;
            b.Cwd = a.Cwd;
            b.Name = a.Name;
            b.HostSessionId = a.HostSessionId;
            b.Title = a.Title;
            b.ProjectName = a.ProjectName;
            b.Entrypoint = a.Entrypoint;
            b.Kind = a.Kind;
            b.StatusRaw = a.StatusRaw;
            b.WaitingFor = a.WaitingFor;
            b.StatusAt = a.StatusAt;
            b.Alive = a.Alive;
            b.State = a.State;
            b.Unread = a.Unread;
            b.StateSince = a.StateSince;
            b.LastPrompt = a.LastPrompt;
            b.LastAssistant = a.LastAssistant;
            b.LastTool = a.LastTool;
            b.Model = a.Model;
            b.ErrorText = a.ErrorText;
            b.TodoDone = a.TodoDone;
            b.TodoTotal = a.TodoTotal;
            b.LastActivity = a.LastActivity;
            b.FirstSeen = a.FirstSeen;
            b.TranscriptPath = a.TranscriptPath;
            b.HookEvent = a.HookEvent;
            b.HookMessage = a.HookMessage;
            b.HookAt = a.HookAt;
            return b;
        }

        private static SessState MapTurnState(string status, out string waiting)
        {
            waiting = "";
            switch ((status ?? "").ToLowerInvariant())
            {
                case "inprogress":
                case "in_progress":
                case "running":
                    return SessState.Working;
                case "failed":
                case "error":
                    return SessState.Error;
                case "interrupted":
                    waiting = "已中止";
                    return SessState.Done;
                default:
                    return SessState.Done;
            }
        }

        private static string AgentMessageText(string json)
        {
            Dictionary<string, object> d = Json.ParseObject(json);
            return d == null ? "" : Json.Str(d, "text");
        }

        private static string UserMessageText(string json)
        {
            Dictionary<string, object> d = Json.ParseObject(json);
            if (d == null) return "";
            object[] content = Json.List(d, "content");
            if (content == null) return Json.Str(d, "text");

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < content.Length; i++)
            {
                Dictionary<string, object> b = content[i] as Dictionary<string, object>;
                if (b == null) continue;
                string t = Json.Str(b, "text");
                if (t.Length == 0) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(t);
            }
            return sb.ToString();
        }

        private static string FindDb(string dir, string prefix)
        {
            try
            {
                string[] files = Directory.GetFiles(dir, prefix + "*.sqlite");
                string best = null;
                int bestN = -1;
                for (int i = 0; i < files.Length; i++)
                {
                    string name = Path.GetFileNameWithoutExtension(files[i]);
                    if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    int n;
                    if (int.TryParse(name.Substring(prefix.Length), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out n) && n > bestN)
                    {
                        bestN = n;
                        best = files[i];
                    }
                }
                return best;
            }
            catch { return null; }
        }

        private static DateTime FromMs(long ms)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms);
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
                        if (et == "task_started")
                        {
                            p.AskedUser = false;
                            p.PendingSyncCallId = "";
                        }
                        p.State = SessState.Working;
                        p.WaitingFor = "";
                    }
                    else if (et == "task_complete")
                    {
                        // 异步提问不会阻塞这一轮：提问瞬间给过 NeedsInput 提醒后，
                        // turn 结束就回到 Done，不能一直停在「等待你回答」
                        p.State = SessState.Done;
                        p.AskedUser = false;
                        p.PendingSyncCallId = "";
                        p.WaitingFor = "";
                        string m = Json.Str(payload, "last_agent_message");
                        if (m.Length > 0) p.LastAssistant = Cap(m, 4000);
                    }
                    else if (et == "turn_aborted")
                    {
                        p.AskedUser = false;
                        p.PendingSyncCallId = "";
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
                        p.AskedUser = false;
                        p.PendingSyncCallId = "";
                        if (p.State == SessState.NeedsInput) { p.State = SessState.Working; p.WaitingFor = ""; }
                    }
                }
                else if (type == "response_item")
                {
                    // 批准之后出现新的条目（工具输出等）⇒ 已经恢复工作；
                    // 但正在等用户回答时要保持 NeedsInput
                    if (!p.AskedUser && p.State == SessState.NeedsInput)
                    {
                        p.State = SessState.Working;
                        p.WaitingFor = "";
                    }

                    string pt = Json.Str(payload, "type");
                    if (pt == "message")
                    {
                        string role = Json.Str(payload, "role");
                        string text2 = ExtractText(payload);
                        if (text2.Length > 0)
                        {
                            if (role == "assistant") p.LastAssistant = Cap(text2, 4000);
                            else if (role == "user")
                            {
                                p.LastPrompt = Cap(text2, 2000);
                                p.AskedUser = false;
                                p.PendingSyncCallId = "";
                            }
                        }
                    }
                    else if (pt == "function_call" || pt == "custom_tool_call")
                    {
                        string name = Json.Str(payload, "name");
                        if (name.Length > 0) p.LastTool = name;

                        if (name == "request_user_input_async" || name == "request_user_input")
                        {
                            p.AskedUser = true;
                            p.State = SessState.NeedsInput;
                            string q = ExtractQuestion(payload);
                            p.WaitingFor = q.Length > 0 ? q : "等待你回答问题";
                            if (q.Length > 0) p.LastAssistant = Cap(q, 4000);
                            if (name == "request_user_input")
                                p.PendingSyncCallId = Json.Str(payload, "call_id");
                        }
                    }
                    else if (pt == "function_call_output" || pt == "custom_tool_call_output")
                    {
                        // 同步 request_user_input 拿到答案后恢复工作
                        if (p.PendingSyncCallId.Length > 0 &&
                            string.Equals(Json.Str(payload, "call_id"), p.PendingSyncCallId, StringComparison.Ordinal))
                        {
                            p.PendingSyncCallId = "";
                            p.AskedUser = false;
                            p.State = SessState.Working;
                            p.WaitingFor = "";
                        }
                    }
                }
            }

            return p;
        }

        private static string ExtractQuestion(Dictionary<string, object> payload)
        {
            object raw = payload.ContainsKey("arguments") ? payload["arguments"] : null;
            Dictionary<string, object> d = raw as Dictionary<string, object>;
            if (d == null && raw is string)
                d = Json.ParseObject((string)raw);
            if (d == null) return "";

            object[] qs = Json.List(d, "questions");
            if (qs == null || qs.Length == 0) return "";

            Dictionary<string, object> q = qs[0] as Dictionary<string, object>;
            if (q == null) return "";
            return Json.Str(q, "title").Trim();
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
