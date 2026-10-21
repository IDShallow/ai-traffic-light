using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ClaudeTrafficLight
{
    /// <summary>
    /// 增量读 Claude 会话记录（.jsonl）。文件可能上百 MB：只读尾部 + 每次只读新增部分。
    /// </summary>
    internal sealed class TranscriptReader
    {
        private const long TailWindow = 512 * 1024;
        private const int MaxLineBytes = 1024 * 1024;      // 超过 1MB 的单行不解析（展示用不到）
        private const int MaxKeepChars = 4000;             // 保留的文本上限，防止大段输出把内存撑大
        private const long MaxReadPerPoll = 1024 * 1024;
        private const int MaxLinesPerChunk = 300;          // 一次只关心最后 300 条：状态展示只要最新的

        public string Path = "";
        private long _offset;
        private bool _headScanned;

        public string Title = "";
        public string FirstPrompt = "";
        public string LastPrompt = "";
        public string LastAssistant = "";
        public string LastTool = "";
        public string Model = "";
        public string ErrorText = "";
        public DateTime ErrorAt = DateTime.MinValue;
        public DateTime LastEntryAt = DateTime.MinValue;
        public int TodoDone;
        public int TodoTotal;
        public int Entries;

        /// <summary>转录里最后一条记录表示的阶段：true=这一轮还没结束。</summary>
        public bool PhaseWorking;
        public DateTime PhaseAt = DateTime.MinValue;
        public string PhaseKind = "";

        private void SetPhase(bool working, DateTime ts, string kind)
        {
            if (ts == DateTime.MinValue)
            {
                // last-prompt 这类条目没有 timestamp：只更新阶段，别用"现在"污染时间边界，
                // 否则启动时扫到一条旧 last-prompt 就会把已完成会话误判成工作中。
                PhaseWorking = working;
                PhaseKind = kind;
                return;
            }
            if (PhaseAt != DateTime.MinValue && ts < PhaseAt) return;
            PhaseWorking = working;
            PhaseAt = ts;
            PhaseKind = kind;
        }

        public TranscriptReader(string path)
        {
            Path = path;
        }

        private FileStream Open()
        {
            return new FileStream(Path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
        }

        public void Refresh()
        {
            if (string.IsNullOrEmpty(Path)) return;

            long len;
            try
            {
                FileInfo fi = new FileInfo(Path);
                if (!fi.Exists) return;
                len = fi.Length;
            }
            catch { return; }

            if (len < _offset)
            {
                // 文件被截断 / 轮换
                _offset = 0;
                _headScanned = false;
                PhaseWorking = false;
                PhaseAt = DateTime.MinValue;
                PhaseKind = "";
            }

            if (_offset == 0 && len > TailWindow)
                Prime(len);

            if (len <= _offset) return;

            int read;
            byte[] buf;
            bool capped = false;
            try
            {
                using (FileStream fs = Open())
                {
                    long toRead = fs.Length - _offset;
                    if (toRead <= 0) return;
                    if (toRead > MaxReadPerPoll) { toRead = MaxReadPerPoll; capped = true; }
                    fs.Seek(_offset, SeekOrigin.Begin);
                    buf = new byte[toRead];
                    read = 0;
                    while (read < toRead)
                    {
                        int n = fs.Read(buf, read, (int)(toRead - read));
                        if (n <= 0) break;
                        read += n;
                    }
                }
            }
            catch { return; }

            if (read <= 0) return;

            int lastNl = -1;
            for (int i = read - 1; i >= 0; i--)
            {
                if (buf[i] == (byte)'\n') { lastNl = i; break; }
            }
            if (lastNl < 0)
            {
                // 单行就超过本次读取上限：丢掉这一行，别让读取永远卡在原地
                if (capped) _offset += read;
                return;
            }

            _offset += lastNl + 1;

            string text = Encoding.UTF8.GetString(buf, 0, lastNl);
            ProcessLines(text, false);
        }

        private void Prime(long len)
        {
            _offset = len - TailWindow;
            try
            {
                using (FileStream fs = Open())
                {
                    fs.Seek(_offset, SeekOrigin.Begin);
                    byte[] b = new byte[8192];
                    int n = fs.Read(b, 0, b.Length);
                    int nl = -1;
                    for (int i = 0; i < n; i++)
                    {
                        if (b[i] == (byte)'\n') { nl = i; break; }
                    }
                    if (nl >= 0) _offset += nl + 1;
                    else _offset = len;
                }
            }
            catch { _offset = len; }

            if (!_headScanned)
            {
                _headScanned = true;
                ScanHead();
            }
        }

        /// <summary>标题 / 首条提示可能在很早的位置，单独扫一遍文件头。</summary>
        private void ScanHead()
        {
            try
            {
                using (FileStream fs = Open())
                {
                    int n = (int)Math.Min(256 * 1024, fs.Length);
                    if (n <= 0) return;
                    byte[] b = new byte[n];
                    int read = 0;
                    while (read < n)
                    {
                        int k = fs.Read(b, read, n - read);
                        if (k <= 0) break;
                        read += k;
                    }
                    string text = Encoding.UTF8.GetString(b, 0, read);
                    ProcessLines(text, true);
                }
            }
            catch { }
        }

        /// <summary>
        /// 快速预筛：值得解析的行。
        /// ⚠ 2.1.x 里 "type" 字段排在 "message" 后面，不能只看行首多少字符。
        /// 这里只做"整行里有没有这个值"的廉价判断；真正的类型以解析后的顶层 type 为准。
        /// </summary>
        private static bool MightBeInteresting(string line)
        {
            return line.IndexOf("\"type\":\"assistant\"", StringComparison.Ordinal) >= 0
                || line.IndexOf("\"type\": \"assistant\"", StringComparison.Ordinal) >= 0
                || line.IndexOf("\"type\":\"user\"", StringComparison.Ordinal) >= 0
                || line.IndexOf("\"type\": \"user\"", StringComparison.Ordinal) >= 0
                || line.IndexOf("\"type\":\"ai-title\"", StringComparison.Ordinal) >= 0
                || line.IndexOf("\"type\": \"ai-title\"", StringComparison.Ordinal) >= 0
                || line.IndexOf("\"type\":\"last-prompt\"", StringComparison.Ordinal) >= 0
                || line.IndexOf("\"type\": \"last-prompt\"", StringComparison.Ordinal) >= 0
                || line.IndexOf("\"type\":\"summary\"", StringComparison.Ordinal) >= 0
                || line.IndexOf("\"type\": \"summary\"", StringComparison.Ordinal) >= 0
                || line.IndexOf("\"type\":\"system\"", StringComparison.Ordinal) >= 0
                || line.IndexOf("\"type\": \"system\"", StringComparison.Ordinal) >= 0;
        }

        private static string Cap(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max);
        }

        private void ProcessLines(string text, bool headOnly)
        {
            int start = 0;

            // 大块增量里只解析最后 N 行：前面那些早就被后面的状态覆盖了
            if (!headOnly)
            {
                int seen = 0;
                for (int i = text.Length - 1; i >= 0; i--)
                {
                    if (text[i] == '\n')
                    {
                        seen++;
                        if (seen > MaxLinesPerChunk) { start = i + 1; break; }
                    }
                }
            }

            while (start < text.Length)
            {
                int nl = text.IndexOf('\n', start);
                string line;
                if (nl < 0)
                {
                    line = text.Substring(start);
                    start = text.Length;
                }
                else
                {
                    line = text.Substring(start, nl - start);
                    start = nl + 1;
                }

                if (line.Length == 0 || line.Length > MaxLineBytes) continue;
                if (line[line.Length - 1] == '\r') line = line.Substring(0, line.Length - 1);

                if (!MightBeInteresting(line)) continue;

                Dictionary<string, object> o = Json.ParseObject(line);
                if (o == null) continue;

                string type = Json.Str(o, "type");
                if (type != "assistant" && type != "user" && type != "ai-title" &&
                    type != "last-prompt" && type != "summary" && type != "system") continue;

                Entries++;
                DateTime ts = ParseTs(Json.Str(o, "timestamp"));
                if (ts > LastEntryAt) LastEntryAt = ts;

                if (type == "ai-title")
                {
                    string t = Json.Str(o, "aiTitle");
                    if (t.Length > 0) Title = Cap(t, 200);
                    continue;
                }

                if (type == "last-prompt")
                {
                    string p = Json.Str(o, "lastPrompt");
                    if (p.Length > 0) LastPrompt = Cap(p, 2000);
                    if (!headOnly) SetPhase(true, ts, "prompt");
                    continue;
                }

                if (type == "summary")
                {
                    if (Title.Length == 0)
                    {
                        string s = Json.Str(o, "summary");
                        if (s.Length > 0) Title = Cap(s, 200);
                    }
                    continue;
                }

                if (headOnly && FirstPrompt.Length == 0 && type == "user")
                {
                    Dictionary<string, object> hm = Json.Map(o, "message");
                    if (hm != null)
                    {
                        string hs = hm.ContainsKey("content") ? hm["content"] as string : null;
                        if (!string.IsNullOrEmpty(hs)) FirstPrompt = Cap(hs, 2000);
                    }
                }

                if (headOnly) continue;

                bool sidechain = Json.Bool(o, "isSidechain", false);
                if (sidechain) continue;

                if (type == "assistant")
                {
                    Dictionary<string, object> msg = Json.Map(o, "message");
                    if (msg == null) continue;

                    string model = Json.Str(msg, "model");
                    if (model.Length > 0) Model = model;

                    string apiErr = ExtractText(msg);
                    if (Json.Bool(o, "isApiErrorMessage", false))
                    {
                        ErrorText = Cap(apiErr, 2000);
                        ErrorAt = ts;
                        if (ErrorText.Length == 0) ErrorText = "API 返回错误";
                    }

                    object[] blocks = msg.ContainsKey("content") ? msg["content"] as object[] : null;
                    bool hasToolUse = false;
                    if (blocks != null)
                    {
                        for (int i = 0; i < blocks.Length; i++)
                        {
                            Dictionary<string, object> b = blocks[i] as Dictionary<string, object>;
                            if (b == null) continue;
                            string bt = Json.Str(b, "type");
                            if (bt == "text")
                            {
                                string t = Json.Str(b, "text");
                                if (t.Trim().Length > 0) LastAssistant = Cap(t, MaxKeepChars);
                            }
                            else if (bt == "tool_use")
                            {
                                hasToolUse = true;
                                string name = Json.Str(b, "name");
                                if (name.Length > 0) LastTool = name;
                                if (name == "TodoWrite") ReadTodos(b);
                            }
                        }
                    }

                    // 阶段：有工具调用 / stop_reason=tool_use ⇒ 还在这一轮里；
                    // 明确的 end_turn 才算这一轮结束。
                    string stop = Json.Str(msg, "stop_reason");
                    if (hasToolUse || stop == "tool_use") SetPhase(true, ts, "tool_use");
                    else if (stop == "end_turn") SetPhase(false, ts, "end_turn");
                }
                else if (type == "user")
                {
                    Dictionary<string, object> msg = Json.Map(o, "message");
                    if (msg == null) continue;
                    if (msg.ContainsKey("content"))
                    {
                        string s = msg["content"] as string;
                        if (s != null)
                        {
                            if (s.Trim().Length > 0) LastPrompt = Cap(s, 2000);
                            SetPhase(true, ts, "prompt");
                        }
                        else
                        {
                            object[] blocks = msg["content"] as object[];
                            if (blocks != null)
                            {
                                for (int i = 0; i < blocks.Length; i++)
                                {
                                    Dictionary<string, object> b = blocks[i] as Dictionary<string, object>;
                                    if (b == null) continue;
                                    if (Json.Str(b, "type") == "tool_result")
                                    {
                                        // 工具回来了，模型还要继续 ⇒ 这一轮没结束
                                        SetPhase(true, ts, "tool_result");
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private static string ExtractText(Dictionary<string, object> msg)
        {
            object content = msg.ContainsKey("content") ? msg["content"] : null;
            string s = content as string;
            if (s != null) return s;

            object[] blocks = content as object[];
            if (blocks == null) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < blocks.Length; i++)
            {
                Dictionary<string, object> b = blocks[i] as Dictionary<string, object>;
                if (b == null) continue;
                if (Json.Str(b, "type") == "text")
                {
                    string t = Json.Str(b, "text");
                    if (t.Length > 0)
                    {
                        if (sb.Length > 0) sb.Append('\n');
                        sb.Append(t);
                    }
                }
            }
            return sb.ToString();
        }

        private void ReadTodos(Dictionary<string, object> toolUse)
        {
            Dictionary<string, object> input = Json.Map(toolUse, "input");
            if (input == null) return;
            object[] todos = Json.List(input, "todos");
            if (todos == null) return;

            int done = 0, total = 0;
            for (int i = 0; i < todos.Length; i++)
            {
                Dictionary<string, object> t = todos[i] as Dictionary<string, object>;
                if (t == null) continue;
                total++;
                if (Json.Str(t, "status") == "completed") done++;
            }
            TodoDone = done;
            TodoTotal = total;
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
