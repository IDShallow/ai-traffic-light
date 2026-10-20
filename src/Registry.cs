using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace ClaudeTrafficLight
{
    internal sealed class RegistryRec
    {
        public string FilePath = "";
        public DateTime FileTime = DateTime.MinValue;
        public int Pid;
        public string SessionId = "";
        public string Cwd = "";
        public string Status = "";
        public string WaitingFor = "";
        public DateTime StatusAt = DateTime.MinValue;
        public DateTime UpdatedAt = DateTime.MinValue;
        public string Entrypoint = "";
        public string Kind = "";
        public string Name = "";
        public string ProcStart = "";
        public bool Alive;
        public DateTime CachedAt = DateTime.MinValue;
    }

    /// <summary>读 %USERPROFILE%\.claude\sessions\*.json —— 每个活着的 Claude 进程一份。</summary>
    internal sealed class RegistryReader
    {
        private sealed class LiveCache
        {
            public bool Alive;
            public DateTime At;
        }

        private readonly Dictionary<string, RegistryRec> _cache = new Dictionary<string, RegistryRec>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, LiveCache> _live = new Dictionary<int, LiveCache>();

        public List<RegistryRec> Read()
        {
            List<RegistryRec> list = new List<RegistryRec>();
            if (!Directory.Exists(Paths.SessionsDir)) return list;

            string[] files;
            try { files = Directory.GetFiles(Paths.SessionsDir, "*.json"); }
            catch { return list; }

            HashSet<string> present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < files.Length; i++)
            {
                string f = files[i];
                present.Add(f);
                DateTime mt;
                try { mt = File.GetLastWriteTimeUtc(f); }
                catch { continue; }

                RegistryRec rec;
                if (!_cache.TryGetValue(f, out rec) || rec.FileTime != mt)
                {
                    rec = Parse(f, mt);
                    _cache[f] = rec;
                }
                if (rec == null || rec.Pid <= 0 || string.IsNullOrEmpty(rec.SessionId)) continue;

                if (DateTime.UtcNow - rec.CachedAt > TimeSpan.FromSeconds(5))
                {
                    rec.Alive = CheckAlive(rec.Pid, rec.ProcStart);
                    rec.CachedAt = DateTime.UtcNow;
                }
                if (!rec.Alive) continue;
                list.Add(rec);
            }

            // 清理已删除文件的缓存
            List<string> gone = new List<string>();
            foreach (string k in _cache.Keys)
            {
                if (!present.Contains(k)) gone.Add(k);
            }
            for (int i = 0; i < gone.Count; i++) _cache.Remove(gone[i]);

            return list;
        }

        private static RegistryRec Parse(string path, DateTime mtime)
        {
            RegistryRec r = new RegistryRec();
            r.FilePath = path;
            r.FileTime = mtime;
            try
            {
                string text = File.ReadAllText(path);
                Dictionary<string, object> d = Json.ParseObject(text);
                if (d == null) return r;

                r.Pid = (int)Json.Long(d, "pid", 0);
                r.SessionId = Json.Str(d, "sessionId");
                r.Cwd = Json.Str(d, "cwd");
                r.Status = Json.Str(d, "status");
                r.WaitingFor = Json.Str(d, "waitingFor");
                r.Entrypoint = Json.Str(d, "entrypoint");
                r.Kind = Json.Str(d, "kind");
                r.Name = Json.Str(d, "name");
                r.ProcStart = Json.Str(d, "procStart");

                long statusMs = Json.Long(d, "statusUpdatedAt", 0);
                if (statusMs <= 0) statusMs = Json.Long(d, "updatedAt", 0);
                if (statusMs > 0) r.StatusAt = FromMs(statusMs);

                long updMs = Json.Long(d, "updatedAt", 0);
                if (updMs > 0) r.UpdatedAt = FromMs(updMs);
            }
            catch
            {
                // 空文件 / 写到一半 —— 下次再说
            }
            return r;
        }

        public static DateTime FromMs(long ms)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms);
        }

        private bool CheckAlive(int pid, string procStart)
        {
            LiveCache c;
            if (_live.TryGetValue(pid, out c) && (DateTime.UtcNow - c.At).TotalSeconds < 5)
                return c.Alive;

            bool alive = false;
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    alive = string.Equals(p.ProcessName, "claude", StringComparison.OrdinalIgnoreCase);
                    if (alive && !string.IsNullOrEmpty(procStart))
                    {
                        long expect;
                        if (long.TryParse(procStart, NumberStyles.Integer, CultureInfo.InvariantCulture, out expect))
                        {
                            long actual = p.StartTime.ToFileTimeUtc();
                            long diff = actual - expect;
                            // 100ns 单位，容忍 2 秒
                            if (diff > 20000000L || diff < -20000000L) alive = false;
                        }
                    }
                }
            }
            catch
            {
                alive = false;
            }

            LiveCache nc = new LiveCache();
            nc.Alive = alive;
            nc.At = DateTime.UtcNow;
            _live[pid] = nc;
            return alive;
        }
    }
}
