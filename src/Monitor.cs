using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace ClaudeTrafficLight
{
    internal sealed class MonitorEvent
    {
        public string Kind = "";
        public string Text = "";
        public SessionInfo Session;
    }

    /// <summary>
    /// 后台轮询：会话注册表 + 会话记录 + hooks 事件 → 快照。
    /// UI 线程只读快照，不碰文件。
    /// </summary>
    internal sealed class Monitor : IDisposable
    {
        private sealed class State
        {
            public SessionInfo Info = new SessionInfo();
            public TranscriptReader Reader;
            public bool FirstPoll = true;
            public DateTime LastNotify = DateTime.MinValue;
            public string LastNotifyKind = "";
            public DateTime LastNotifyHookAt = DateTime.MinValue;
            public DateTime DeadAt = DateTime.MinValue;
        }

        private sealed class ExtState
        {
            public SessState Last = SessState.Offline;
            public bool Unread;
            public bool First = true;
            public DateTime LastNotify = DateTime.MinValue;
            public string LastKind = "";
            public DateTime LastSeen = DateTime.MinValue;
        }

        private readonly object _sync = new object();
        private readonly Dictionary<string, State> _states = new Dictionary<string, State>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ExtState> _extStates = new Dictionary<string, ExtState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, object>> _hooks =
            new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _transcriptIndex =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentQueue<MonitorEvent> _events = new ConcurrentQueue<MonitorEvent>();
        private readonly RegistryReader _registry = new RegistryReader();
        private readonly AppSettings _settings;

        private volatile List<SessionInfo> _snapshot = new List<SessionInfo>();
        private Thread _thread;
        private volatile bool _stop;
        private DateTime _lastIndexScan = DateTime.MinValue;
        private DateTime _lastHookCleanup = DateTime.MinValue;
        private int _version;

        public Monitor(AppSettings settings)
        {
            _settings = settings;
        }

        public List<SessionInfo> Snapshot { get { return _snapshot; } }
        public int Version { get { return _version; } }

        public bool TryDequeue(out MonitorEvent e)
        {
            return _events.TryDequeue(out e);
        }

        public void Start()
        {
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "claude-monitor";
            _thread.Start();
        }

        private void Loop()
        {
            Stopwatch sw = new Stopwatch();
            long totalMs = 0;
            int n = 0;

            while (!_stop)
            {
                sw.Reset();
                sw.Start();
                try { Poll(); }
                catch (Exception ex)
                {
                    if ((DateTime.UtcNow - _lastErrorLog).TotalMinutes >= 1)
                    {
                        _lastErrorLog = DateTime.UtcNow;
                        Log.Write("poll error: " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
                sw.Stop();

                totalMs += sw.ElapsedMilliseconds;
                n++;
                if (n >= 60)
                {
                    long avg = totalMs / n;
                    if (avg >= 30) Log.Write("poll avg " + avg + "ms over " + n + " rounds (" + _lastBreakdown + ")");
                    totalMs = 0;
                    n = 0;
                }

                Thread.Sleep(800);
            }
        }

        private DateTime _lastErrorLog = DateTime.MinValue;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private string _lastBreakdown = "";

        public void Dispose()
        {
            _stop = true;
        }

        private void Poll()
        {
            DateTime now = DateTime.UtcNow;
            long m0 = _clock.ElapsedMilliseconds;
            List<RegistryRec> recs = new List<RegistryRec>();
            if (_settings.MonitorClaude)
            {
                ConsumeHooks(now);
                recs = _registry.Read();
            }
            long m1 = _clock.ElapsedMilliseconds;
            long m2 = _clock.ElapsedMilliseconds;

            HashSet<string> aliveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < recs.Count; i++)
            {
                RegistryRec r = recs[i];
                if ((i % 10) == 0) Thread.Sleep(0);
                aliveIds.Add(r.SessionId);

                State st;
                lock (_sync)
                {
                    if (!_states.TryGetValue(r.SessionId, out st))
                    {
                        st = new State();
                        st.Info.SessionId = r.SessionId;
                        st.Info.FirstSeen = now;
                        st.FirstPoll = true;
                        _states[r.SessionId] = st;
                    }
                }

                st.Info.Pid = r.Pid;
                st.Info.Cwd = r.Cwd;
                st.Info.ProjectName = Ui.BaseName(r.Cwd);
                st.Info.Entrypoint = r.Entrypoint;
                st.Info.Kind = r.Kind;
                st.Info.Name = r.Name;
                st.Info.HostSessionId = r.HostSessionId;
                st.Info.StatusRaw = r.Status;
                st.Info.WaitingFor = r.WaitingFor;
                st.Info.StatusAt = r.StatusAt;
                st.Info.Alive = true;
                st.DeadAt = DateTime.MinValue;

                Dictionary<string, object> hook = null;
                _hooks.TryGetValue(r.SessionId, out hook);
                ApplyHook(st.Info, hook);

                string tp = Json.Str(hook, "transcript_path");
                if (string.IsNullOrEmpty(tp)) tp = ResolveTranscript(r.SessionId, r.Cwd, now);
                if (!string.IsNullOrEmpty(tp))
                {
                    st.Info.TranscriptPath = tp;
                    if (st.Reader == null || !string.Equals(st.Reader.Path, tp, StringComparison.OrdinalIgnoreCase))
                        st.Reader = new TranscriptReader(tp);
                    try { st.Reader.Refresh(); }
                    catch { }
                }

                PullReader(st);

                if (st.Info.LastActivity < r.StatusAt) st.Info.LastActivity = r.StatusAt;
                if (hook != null)
                {
                    DateTime ha = HookAt(hook);
                    if (ha > st.Info.LastActivity) st.Info.LastActivity = ha;
                }

                SessState s = Compute(st, r, hook);
                st.Info.Muted = _settings.IsMuted(st.Info);
                ApplyState(st, s, hook, now);
            }

            // 消失的会话
            List<string> remove = new List<string>();
            lock (_sync)
            {
                foreach (KeyValuePair<string, State> kv in _states)
                {
                    State st = kv.Value;
                    if (aliveIds.Contains(kv.Key))
                    {
                        st.FirstPoll = false;
                        continue;
                    }
                    if (st.Info.Alive)
                    {
                        st.Info.Alive = false;
                        st.Info.State = SessState.Offline;
                        st.Info.StateSince = now;
                        st.DeadAt = now;
                    }
                    if (st.DeadAt != DateTime.MinValue && (now - st.DeadAt).TotalMinutes > 30)
                        remove.Add(kv.Key);
                }
                for (int i = 0; i < remove.Count; i++) _states.Remove(remove[i]);
            }

            // 快照
            long m3 = _clock.ElapsedMilliseconds;
            List<SessionInfo> snap = new List<SessionInfo>();
            lock (_sync)
            {
                foreach (KeyValuePair<string, State> kv in _states)
                {
                    State st = kv.Value;
                    if (st.Info.Alive)
                        snap.Add(Clone(st.Info));
                    else if (_settings.ShowOffline && st.DeadAt != DateTime.MinValue &&
                             (now - st.DeadAt).TotalMinutes <= 30)
                        snap.Add(Clone(st.Info));
                }
            }
            // Codex / DeepSeek Harness
            List<SessionInfo> ext = new List<SessionInfo>();
            try { ext.AddRange(CodexProvider.Scan(_settings, now)); } catch { }
            try { ext.AddRange(DshProvider.Scan(_settings, now)); } catch { }
            ApplyExternalStates(ext, now);
            snap.AddRange(ext);

            snap.Sort(CompareSessions);

            // 只有内容真的变了（或过了 30 秒，让"N 分钟前"有机会刷新）才让 UI 重绘
            long sig = Signature(snap);
            if (sig != _signature || (now - _lastSnapshotAt).TotalSeconds >= 30)
            {
                _signature = sig;
                _lastSnapshotAt = now;
                _snapshot = snap;
                Interlocked.Increment(ref _version);
            }

            CleanupHooks(now);

            long m4 = _clock.ElapsedMilliseconds;
            _lastBreakdown = "hooks=" + (m1 - m0) + " reg=" + (m2 - m1) + " tx=" + (m3 - m2) +
                             " snap=" + (m4 - m3) + " n=" + recs.Count;
        }

        private long _signature;
        private DateTime _lastSnapshotAt = DateTime.MinValue;

        private static long Signature(List<SessionInfo> snap)
        {
            long h = 1469598103934665603L;
            for (int i = 0; i < snap.Count; i++)
            {
                SessionInfo s = snap[i];
                h = Mix(h, s.Provider);
                h = Mix(h, s.Name);
                h = Mix(h, s.HostSessionId);
                h = Mix(h, s.SessionId);
                h = Mix(h, (int)s.State);
                h = Mix(h, s.Unread ? 1 : 0);
                h = Mix(h, s.Alive ? 1 : 0);
                h = Mix(h, s.Pid);
                h = Mix(h, s.Title);
                h = Mix(h, s.LastPrompt == null ? 0 : s.LastPrompt.Length);
                h = Mix(h, s.LastAssistant == null ? 0 : s.LastAssistant.Length);
                h = Mix(h, s.LastTool);
                h = Mix(h, s.Model);
                h = Mix(h, s.TodoDone);
                h = Mix(h, s.TodoTotal);
                h = Mix(h, s.LastActivity.Ticks / TimeSpan.TicksPerSecond);
                h = Mix(h, s.ProjectName);
                h = Mix(h, s.WaitingFor);
                h = Mix(h, s.ErrorText == null ? 0 : s.ErrorText.Length);
                h = Mix(h, s.PendingQuestion);
                h = Mix(h, s.PendingHeader);
                h = Mix(h, s.PendingOptions == null ? 0 : s.PendingOptions.Length);
                if (s.PendingOptions != null)
                {
                    for (int k = 0; k < s.PendingOptions.Length; k++) h = Mix(h, s.PendingOptions[k]);
                }
            }
            return h;
        }

        private static long Mix(long h, long v)
        {
            unchecked { return (h ^ v) * 1099511628211L; }
        }

        private static long Mix(long h, string s)
        {
            if (string.IsNullOrEmpty(s)) return Mix(h, 0L);
            unchecked
            {
                h = Mix(h, s.Length);
                for (int i = 0; i < s.Length; i++) h = (h ^ s[i]) * 1099511628211L;
            }
            return h;
        }

        private static void PullReader(State st)
        {
            TranscriptReader r = st.Reader;
            if (r == null) return;
            SessionInfo i = st.Info;

            if (r.Title.Length > 0) i.Title = r.Title;
            string prompt = r.LastPrompt.Length > 0 ? r.LastPrompt : r.FirstPrompt;
            if (prompt.Length > 0) i.LastPrompt = prompt;
            if (r.LastAssistant.Length > 0) i.LastAssistant = r.LastAssistant;
            i.LastTool = r.LastTool;
            i.Model = r.Model;
            i.ErrorText = r.ErrorText;
            i.PendingQuestion = r.PendingQuestion;
            i.PendingHeader = r.PendingHeader;
            i.PendingOptions = r.PendingOptions;
            i.TodoDone = r.TodoDone;
            i.TodoTotal = r.TodoTotal;
            if (r.LastEntryAt > i.LastActivity) i.LastActivity = r.LastEntryAt;
        }

        private static void ApplyHook(SessionInfo i, Dictionary<string, object> hook)
        {
            if (hook == null) return;
            i.HookEvent = Json.Str(hook, "hook_event_name");
            i.HookMessage = Json.Str(hook, "message");
            i.HookAt = HookAt(hook);
        }

        private static DateTime HookAt(Dictionary<string, object> hook)
        {
            long ticks = Json.Long(hook, "_at", 0);
            if (ticks <= 0) return DateTime.MinValue;
            try { return new DateTime(ticks, DateTimeKind.Utc); }
            catch { return DateTime.MinValue; }
        }

        private static SessState FromRegistry(RegistryRec r)
        {
            if (r.Status == "busy") return SessState.Working;
            if (r.Status == "waiting") return SessState.NeedsInput;
            return SessState.Done;
        }

        private static SessState Compute(State st, RegistryRec r, Dictionary<string, object> hook)
        {
            if (!r.Alive) return SessState.Offline;

            DateTime hookAt = HookAt(hook);
            bool hookWins = hookAt != DateTime.MinValue && hookAt >= r.StatusAt;

            SessState s;
            if (hookWins)
            {
                string ev = Json.Str(hook, "hook_event_name");
                if (ev == "Notification")
                {
                    // 权限弹窗后恢复执行 → 记录会继续增长；此时不再算"等你"
                    bool progressed = st.Reader != null && st.Reader.LastEntryAt != DateTime.MinValue &&
                                      st.Reader.LastEntryAt > hookAt.AddSeconds(1);
                    s = (progressed && r.Status == "busy") ? SessState.Working : SessState.NeedsInput;
                }
                else if (ev == "Stop") s = SessState.Done;
                else if (ev == "UserPromptSubmit" || ev == "SessionStart") s = SessState.Working;
                else if (ev == "StopFailure") s = SessState.Error;
                else if (ev == "SessionEnd") s = SessState.Offline;
                else s = FromRegistry(r);
            }
            else
            {
                s = FromRegistry(r);
            }

            // 记录里的 API 错误：只在"这一轮已经停下"时才算异常；
            // 如果注册表说还在忙，说明已经恢复/重试了，别一直挂着红灯。
            if (s != SessState.NeedsInput && s != SessState.Working && st.Reader != null &&
                st.Reader.ErrorAt != DateTime.MinValue &&
                (DateTime.UtcNow - st.Reader.ErrorAt).TotalMinutes < 3)
                s = SessState.Error;

            DateTime boundary = r.StatusAt;
            if (hookAt != DateTime.MinValue && hookAt > boundary) boundary = hookAt;

            // 待回答的 AskUserQuestion：比注册表状态 / Stop 钩子更新 ⇒ 等你回答
            if (st.Reader != null && st.Reader.HasPendingQuestion &&
                s != SessState.Error && s != SessState.Offline &&
                (st.Reader.PendingAt == DateTime.MinValue || boundary == DateTime.MinValue ||
                 st.Reader.PendingAt > boundary))
                s = SessState.NeedsInput;

            // 注册表的 status 只在回合边界写；如果转录里已经有"新回合/工具在跑"的记录，
            // 且比 statusUpdatedAt（或 Stop 钩子）更晚，说明注册表是旧的 ⇒ 按工作中算。
            if (s == SessState.Done && st.Reader != null && st.Reader.PhaseWorking &&
                st.Reader.PhaseAt != DateTime.MinValue &&
                (boundary == DateTime.MinValue || st.Reader.PhaseAt > boundary))
                s = SessState.Working;

            return s;
        }

        private void ApplyState(State st, SessState s, Dictionary<string, object> hook, DateTime now)
        {
            SessState prev = st.Info.State;
            bool first = st.FirstPoll;

            if (s != prev)
            {
                st.Info.State = s;
                st.Info.StateSince = now;
                if (!first)
                    Log.Write("state " + st.Info.SessionId.Substring(0, Math.Min(8, st.Info.SessionId.Length)) +
                              " " + prev + " -> " + s);

                if (s == SessState.Working)
                {
                    st.Info.Unread = false;
                }
                else if (s == SessState.NeedsInput || s == SessState.Error)
                {
                    if (!first && !st.Info.Muted) st.Info.Unread = true;
                    if (!first) MaybeNotify(st, s, hook, now);
                }
                else if (s == SessState.Done)
                {
                    if (!first && prev == SessState.Working)
                    {
                        if (!st.Info.Muted) st.Info.Unread = true;
                        MaybeNotify(st, s, hook, now);
                    }
                }
            }
            else if (s == SessState.NeedsInput && !first && hook != null &&
                     HookAt(hook) > st.LastNotifyHookAt)
            {
                // 同一个"等待"里又来了新的通知（新的权限请求等）；
                // 没有新的 hook 事件就不重复轰炸（无钩子时尤其不能每 20 秒弹一次）
                MaybeNotify(st, s, hook, now);
            }

            st.FirstPoll = false;
        }

        private void ApplyExternalStates(List<SessionInfo> list, DateTime now)
        {
            lock (_sync)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    SessionInfo s = list[i];
                    string key = s.Provider + ":" + s.SessionId;
                    s.Muted = _settings.IsMuted(s);

                    ExtState st;
                    if (!_extStates.TryGetValue(key, out st))
                    {
                        st = new ExtState();
                        _extStates[key] = st;
                    }
                    st.LastSeen = now;

                    bool first = st.First;
                    SessState prev = st.Last;

                    if (s.State != prev)
                    {
                        if (s.State == SessState.Working)
                        {
                            st.Unread = false;
                        }
                        else if (s.State == SessState.NeedsInput || s.State == SessState.Error)
                        {
                            if (!first && !s.Muted) st.Unread = true;
                            if (!first) NotifyExternal(s, now, st);
                        }
                        else if (s.State == SessState.Done)
                        {
                            if (!first && prev == SessState.Working)
                            {
                                if (!s.Muted) st.Unread = true;
                                NotifyExternal(s, now, st);
                            }
                        }
                        st.Last = s.State;
                    }
                    else if (s.State == SessState.NeedsInput && !first)
                    {
                        NotifyExternal(s, now, st);
                    }

                    s.Unread = st.Unread;
                    st.First = false;
                }

                if (_extStates.Count > 40)
                {
                    List<string> stale = new List<string>();
                    foreach (KeyValuePair<string, ExtState> kv in _extStates)
                    {
                        if ((now - kv.Value.LastSeen).TotalHours > 2) stale.Add(kv.Key);
                    }
                    for (int i = 0; i < stale.Count; i++) _extStates.Remove(stale[i]);
                }
            }
        }

        private void NotifyExternal(SessionInfo s, DateTime now, ExtState st)
        {
            if (s.Muted) return;
            string kind = s.State == SessState.NeedsInput ? "needs" : (s.State == SessState.Error ? "error" : "done");
            if (st.LastKind == kind && (now - st.LastNotify).TotalSeconds < 20) return;
            st.LastKind = kind;
            st.LastNotify = now;

            MonitorEvent e = new MonitorEvent();
            e.Kind = kind;
            e.Text = ExternalNotifyText(s, kind);
            e.Session = Clone(s);
            _events.Enqueue(e);
            Log.Write("notify " + kind + " [" + s.Provider + "] " + s.DisplayTitle() + " :: " + e.Text);
        }

        private static string ExternalNotifyText(SessionInfo s, string kind)
        {
            if (kind == "needs")
            {
                if (!string.IsNullOrEmpty(s.WaitingFor)) return Ui.Shorten(s.WaitingFor, 180);
                return "需要你的回答";
            }
            if (kind == "done")
            {
                if (!string.IsNullOrEmpty(s.LastAssistant)) return Ui.Shorten(s.LastAssistant, 180);
                return "这一轮已经完成";
            }
            if (!string.IsNullOrEmpty(s.ErrorText)) return Ui.Shorten(s.ErrorText, 180);
            return "会话出现异常";
        }

        private void MaybeNotify(State st, SessState s, Dictionary<string, object> hook, DateTime now)
        {
            if (st.Info.Muted) return;
            string kind = s == SessState.NeedsInput ? "needs" : (s == SessState.Error ? "error" : "done");
            if (st.LastNotifyKind == kind && (now - st.LastNotify).TotalSeconds < 20) return;
            st.LastNotifyKind = kind;
            st.LastNotify = now;
            st.LastNotifyHookAt = HookAt(hook);

            MonitorEvent e = new MonitorEvent();
            e.Kind = kind;
            e.Text = NotifyText(st.Info, kind, hook);
            e.Session = Clone(st.Info);
            _events.Enqueue(e);
            Log.Write("notify " + kind + " " + st.Info.DisplayTitle() + " :: " + e.Text);
        }

        private static string NotifyText(SessionInfo i, string kind, Dictionary<string, object> hook)
        {
            if (kind == "needs")
            {
                string m = Json.Str(hook, "message");
                if (m.Length > 0) return Ui.Shorten(m, 180);
                if (!string.IsNullOrEmpty(i.WaitingFor)) return Ui.Shorten(i.WaitingFor, 180);
                return "Claude 需要你的回答";
            }
            if (kind == "done")
            {
                string m = Json.Str(hook, "last_assistant_message");
                if (m.Length == 0) m = i.LastAssistant;
                if (m.Length > 0) return Ui.Shorten(m, 180);
                return "这一轮已经完成";
            }
            string err = Json.Str(hook, "error");
            if (err.Length == 0) err = i.ErrorText;
            if (err.Length > 0) return Ui.Shorten(err, 180);
            return "会话出现异常";
        }

        private int CompareSessions(SessionInfo a, SessionInfo b)
        {
            if (_settings.SortByState)
            {
                int ra = Rank(a.State), rb = Rank(b.State);
                if (ra != rb) return ra - rb;
            }
            int c = b.LastActivity.CompareTo(a.LastActivity);
            if (c != 0) return c;
            return string.Compare(a.DisplayTitle(), b.DisplayTitle(), StringComparison.OrdinalIgnoreCase);
        }

        private static int Rank(SessState s)
        {
            switch (s)
            {
                case SessState.NeedsInput: return 0;
                case SessState.Error: return 1;
                case SessState.Working: return 2;
                case SessState.Done: return 3;
                default: return 4;
            }
        }

        public void MarkRead(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;

            List<SessionInfo> snap = _snapshot;
            SessionInfo found = null;
            for (int i = 0; i < snap.Count; i++)
            {
                if (string.Equals(snap[i].SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
                {
                    found = snap[i];
                    break;
                }
            }

            lock (_sync)
            {
                if (found != null && found.Provider != "claude")
                {
                    ExtState es;
                    if (_extStates.TryGetValue(found.Provider + ":" + sessionId, out es)) es.Unread = false;
                }
                else
                {
                    State st;
                    if (_states.TryGetValue(sessionId, out st)) st.Info.Unread = false;
                }

                for (int i = 0; i < snap.Count; i++)
                {
                    if (string.Equals(snap[i].SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
                        snap[i].Unread = false;
                }
            }
        }

        private void ConsumeHooks(DateTime now)
        {
            try
            {
                if (!Directory.Exists(Paths.EventsDir)) return;
                string[] files = Directory.GetFiles(Paths.EventsDir, "*.json");
                if (files.Length == 0) return;
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < files.Length; i++)
                {
                    string f = files[i];
                    try
                    {
                        DateTime mt = File.GetLastWriteTimeUtc(f);
                        string text = File.ReadAllText(f);
                        Dictionary<string, object> d = Json.ParseObject(text);
                        if (d != null)
                        {
                            string sid = Json.Str(d, "session_id");
                            if (sid.Length > 0)
                            {
                                d["_at"] = mt.Ticks;
                                Dictionary<string, object> old;
                                if (!_hooks.TryGetValue(sid, out old) ||
                                    mt.Ticks > Json.Long(old, "_at", 0))
                                    _hooks[sid] = d;
                                Log.Write("hook " + Json.Str(d, "hook_event_name") + " " +
                                          sid.Substring(0, Math.Min(8, sid.Length)));
                            }
                        }
                        File.Delete(f);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void CleanupHooks(DateTime now)
        {
            if ((now - _lastHookCleanup).TotalSeconds < 60) return;
            _lastHookCleanup = now;
            List<string> stale = new List<string>();
            foreach (KeyValuePair<string, Dictionary<string, object>> kv in _hooks)
            {
                DateTime at = HookAt(kv.Value);
                if (at != DateTime.MinValue && (now - at).TotalHours > 2) stale.Add(kv.Key);
            }
            for (int i = 0; i < stale.Count; i++) _hooks.Remove(stale[i]);
        }

        private string ResolveTranscript(string sessionId, string cwd, DateTime now)
        {
            string slug = Slug(cwd);
            if (slug.Length > 0)
            {
                string p = Path.Combine(Paths.ProjectsDir, slug, sessionId + ".jsonl");
                try { if (File.Exists(p)) return p; }
                catch { }
            }

            if ((now - _lastIndexScan).TotalSeconds > 15)
            {
                _lastIndexScan = now;
                try
                {
                    string[] dirs = Directory.GetDirectories(Paths.ProjectsDir);
                    for (int i = 0; i < dirs.Length; i++)
                    {
                        string[] fs;
                        try { fs = Directory.GetFiles(dirs[i], "*.jsonl"); }
                        catch { continue; }
                        for (int j = 0; j < fs.Length; j++)
                        {
                            string id = Path.GetFileNameWithoutExtension(fs[j]);
                            _transcriptIndex[id] = fs[j];
                        }
                    }
                }
                catch { }
            }

            string found;
            if (_transcriptIndex.TryGetValue(sessionId, out found)) return found;
            return "";
        }

        private static string Slug(string cwd)
        {
            if (string.IsNullOrEmpty(cwd)) return "";
            return cwd.Replace(":", "-").Replace("\\", "-").Replace("/", "-");
        }

        private static SessionInfo Clone(SessionInfo a)
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
            b.PendingQuestion = a.PendingQuestion;
            b.PendingHeader = a.PendingHeader;
            b.PendingOptions = a.PendingOptions;
            b.TodoDone = a.TodoDone;
            b.TodoTotal = a.TodoTotal;
            b.LastActivity = a.LastActivity;
            b.LastPromptAt = a.LastPromptAt;
            b.FirstSeen = a.FirstSeen;
            b.Muted = a.Muted;
            b.TranscriptPath = a.TranscriptPath;
            b.HookEvent = a.HookEvent;
            b.HookMessage = a.HookMessage;
            b.HookAt = a.HookAt;
            return b;
        }
    }
}
