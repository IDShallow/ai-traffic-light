using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace ClaudeTrafficLight
{
    internal sealed class UpdateInfo
    {
        public string Version = "";
        public string Url = "";
        public string Sha256 = "";
        public string Notes = "";
        public bool Mandatory;
    }

    /// <summary>
    /// 检查更新 / 下载安装包。
    /// 更新源地址按顺序取：%APPDATA%\ClaudeTrafficLight\update.json → exe 同目录 update.json。
    /// 文件内容：{"feedUrl":"https://.../latest.json"}
    /// 清单内容：{"version":"0.2.1","url":"https://.../Setup.exe","sha256":"...","notes":"...","mandatory":false}
    /// </summary>
    internal static class UpdateChecker
    {
        public static string FeedUrl()
        {
            string[] candidates = new string[]
            {
                Path.Combine(Paths.AppData, "update.json"),
                Path.Combine(Path.GetDirectoryName(Paths.ExePath) ?? "", "update.json")
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    if (!File.Exists(candidates[i])) continue;
                    Dictionary<string, object> d = Json.ParseObject(File.ReadAllText(candidates[i]));
                    if (d == null) continue;
                    string u = Json.Str(d, "feedUrl");
                    if (!string.IsNullOrEmpty(u)) return u;
                }
                catch { }
            }
            return AppInfo.DefaultFeedUrl;
        }

        public static UpdateInfo Check()
        {
            string url = FeedUrl();
            if (url.Length == 0)
                throw new Exception("未配置更新源（可在程序目录或 %APPDATA%\\ClaudeTrafficLight\\update.json 里写 feedUrl）");

            string text = GetText(url, 12000);
            Dictionary<string, object> d = Json.ParseObject(text);
            if (d == null) throw new Exception("更新源返回的不是合法 JSON");

            UpdateInfo info = new UpdateInfo();
            info.Version = Json.Str(d, "version");
            info.Url = Json.Str(d, "url");
            info.Sha256 = Json.Str(d, "sha256");
            info.Notes = Json.Str(d, "notes");
            info.Mandatory = Json.Bool(d, "mandatory", false);

            Version remote, current;
            if (!Version.TryParse(Normalize(info.Version), out remote))
                throw new Exception("更新源里的版本号不合法：" + info.Version);
            if (!Version.TryParse(Normalize(AppInfo.Version), out current))
                current = new Version(0, 0);

            if (Compare(remote, current) <= 0) return null;
            if (info.Url.Length == 0) throw new Exception("更新源没有给出下载地址");
            return info;
        }

        /// <summary>只比 Major.Minor.Build；0.2.0 与 0.2.0.0 视为同一版。</summary>
        private static int Compare(Version a, Version b)
        {
            int c = a.Major.CompareTo(b.Major);
            if (c != 0) return c;
            c = a.Minor.CompareTo(b.Minor);
            if (c != 0) return c;
            return Norm(a.Build).CompareTo(Norm(b.Build));
        }

        private static int Norm(int v)
        {
            return v < 0 ? 0 : v;
        }

        private static string Normalize(string v)
        {
            if (string.IsNullOrEmpty(v)) return "0.0";
            v = v.Trim();
            if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase)) v = v.Substring(1);
            int dash = v.IndexOf('-');
            if (dash > 0) v = v.Substring(0, dash);
            return v;
        }

        private static string GetText(string url, int timeoutMs)
        {
            EnableTls();
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "ClaudeTrafficLight/" + AppInfo.Version;
            req.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            using (WebResponse resp = req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }

        private static void EnableTls()
        {
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } // TLS 1.2
            catch { }
        }

        public static string Download(UpdateInfo info, Action<int> progress)
        {
            if (info == null || string.IsNullOrEmpty(info.Url))
                throw new Exception("没有下载地址");

            EnableTls();
            string path = Path.Combine(Path.GetTempPath(), AppInfo.SetupName);

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(info.Url);
            req.Timeout = 20000;
            req.ReadWriteTimeout = 60000;
            req.UserAgent = "ClaudeTrafficLight/" + AppInfo.Version;
            req.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);

            using (WebResponse resp = req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                long total = resp.ContentLength;
                byte[] buf = new byte[64 * 1024];
                long done = 0;
                int n;
                int lastPct = -1;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    fs.Write(buf, 0, n);
                    done += n;
                    if (progress != null && total > 0)
                    {
                        int pct = (int)(done * 100 / total);
                        if (pct != lastPct) { lastPct = pct; progress(pct); }
                    }
                }
            }

            if (!string.IsNullOrEmpty(info.Sha256))
            {
                string actual = Sha256File(path);
                if (!string.Equals(actual, info.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(path); } catch { }
                    throw new Exception("下载文件的 SHA256 与更新源不一致，已丢弃");
                }
            }

            return path;
        }

        public static string Sha256File(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                byte[] hash = sha.ComputeHash(fs);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
