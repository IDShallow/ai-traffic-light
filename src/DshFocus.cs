using System;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace ClaudeTrafficLight
{
    /// <summary>
    /// DeepSeek Harness（Electron / Chromium）里按会话标题选中左侧列表的条目。
    /// UIA 树：Tree → TreeItem（class 含 sessionRow）；SelectionItemPattern.Select() 就是切换会话。
    /// 条目名是「标题 + 相对时间」；同名会话用时间最接近 LastActivity 的那条。
    /// </summary>
    internal static class DshFocus
    {
        private static int _busy;

        public static bool SelectSession(IntPtr mainWindow, string title, DateTime lastActivityUtc)
        {
            if (mainWindow == IntPtr.Zero || string.IsNullOrEmpty(title)) return false;
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return false;

            try
            {
                IntPtr renderer = DesktopFocus.FindRenderer(mainWindow);
                if (renderer == IntPtr.Zero) return false;

                AutomationElement root = AutomationElement.FromHandle(renderer);
                if (root == null) return false;

                // Chromium 的辅助功能树是按需建立的：DSH 会话很大时第一次要几秒，后台线程慢慢等
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    AutomationElement r = attempt == 0 ? root : AutomationElement.FromHandle(renderer);
                    AutomationElement target = FindBest(r, title, lastActivityUtc);
                    if (target != null)
                    {
                        object pattern;
                        if (target.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
                        {
                            ((SelectionItemPattern)pattern).Select();
                            Log.Write("dsh focus selected " + target.Current.Name);
                            return true;
                        }
                    }
                    Thread.Sleep(300);
                }
            }
            catch { }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
            return false;
        }

        private static AutomationElement FindBest(AutomationElement root, string title, DateTime lastActivityUtc)
        {
            Condition cond = new PropertyCondition(
                AutomationElement.ControlTypeProperty, ControlType.TreeItem);
            AutomationElementCollection items = root.FindAll(TreeScope.Descendants, cond);
            if (items == null) return null;

            string want = Norm(title);
            double wantAge = lastActivityUtc == DateTime.MinValue
                ? -1
                : (DateTime.UtcNow - lastActivityUtc).TotalMinutes;

            AutomationElement best = null;
            double bestScore = double.MaxValue;

            for (int i = 0; i < items.Count; i++)
            {
                AutomationElement it = items[i];
                string cls = it.Current.ClassName;
                if (cls == null || cls.IndexOf("sessionRow", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string rowTitle, rowTime;
                ReadTexts(it, out rowTitle, out rowTime);
                string rowName = it.Current.Name ?? "";
                if (rowTitle.Length == 0) rowTitle = StripTime(rowName);
                if (rowTime.Length == 0) rowTime = TimeTail(rowName);

                string got = Norm(rowTitle);
                if (got.Length == 0) continue;

                int titleScore;
                if (got == want) titleScore = 0;
                else if (got.Length >= 4 && want.StartsWith(got, StringComparison.Ordinal)) titleScore = 40;
                else if (want.Length >= 4 && got.StartsWith(want, StringComparison.Ordinal)) titleScore = 40;
                else continue;

                double score = titleScore;
                if (wantAge >= 0)
                {
                    double rowAge = ParseAgo(rowTime);
                    if (rowAge < 0) score += 500;
                    else score += Math.Min(Math.Abs(rowAge - wantAge), 1440);
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    best = it;
                }
            }
            return best;
        }

        /// <summary>条目里的 Text 子节点：第一个是标题，最后一个像"12分"的是时间。</summary>
        private static void ReadTexts(AutomationElement item, out string title, out string time)
        {
            title = "";
            time = "";
            Condition cond = new PropertyCondition(
                AutomationElement.ControlTypeProperty, ControlType.Text);
            AutomationElementCollection texts = item.FindAll(TreeScope.Children, cond);
            if (texts == null || texts.Count == 0) return;

            for (int i = 0; i < texts.Count; i++)
            {
                string n = texts[i].Current.Name;
                if (string.IsNullOrEmpty(n)) continue;
                if (title.Length == 0) title = n;
                if (LooksLikeAgo(n)) time = n;
            }
        }

        private static string StripTime(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int sp = name.LastIndexOf(' ');
            if (sp > 0 && sp < name.Length - 1 && LooksLikeAgo(name.Substring(sp + 1)))
                return name.Substring(0, sp);
            return name;
        }

        /// <summary>条目名末尾的「10天」这类相对时间。</summary>
        private static string TimeTail(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int sp = name.LastIndexOf(' ');
            if (sp > 0 && sp < name.Length - 1)
            {
                string tail = name.Substring(sp + 1);
                if (LooksLikeAgo(tail)) return tail;
            }
            return "";
        }

        private static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace(' ', ' ').Trim();
            while (s.IndexOf("  ", StringComparison.Ordinal) >= 0) s = s.Replace("  ", " ");
            return s;
        }

        private static bool LooksLikeAgo(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (s == "刚刚" || s == "昨天" || s == "前天") return true;
            if (s.EndsWith("分钟") || s.EndsWith("分") ||
                s.EndsWith("小时") || s.EndsWith("天")) return true;
            return s.IndexOf('月') > 0 && s.EndsWith("日");
        }

        /// <summary>把「刚刚 / 12分 / 3小时 / 2天 / 10月8日」换成分钟数；认不出返回 -1。</summary>
        private static double ParseAgo(string s)
        {
            if (string.IsNullOrEmpty(s)) return -1;
            s = s.Trim();
            if (s == "刚刚") return 0;
            if (s == "昨天") return 24 * 60;
            if (s == "前天") return 48 * 60;

            try
            {
                int digits = 0;
                while (digits < s.Length && s[digits] >= '0' && s[digits] <= '9') digits++;
                if (digits > 0)
                {
                    double n = double.Parse(s.Substring(0, digits));
                    if (s.EndsWith("分钟") || s.EndsWith("分")) return n;
                    if (s.EndsWith("小时")) return n * 60;
                    if (s.EndsWith("天")) return n * 1440;
                }

                int mi = s.IndexOf('月');
                int di = s.IndexOf('日');
                if (mi > 0 && di > mi)
                {
                    int month = int.Parse(s.Substring(0, mi));
                    int day = int.Parse(s.Substring(mi + 1, di - mi - 1));
                    DateTime now = DateTime.Now;
                    DateTime d = new DateTime(now.Year, month, day);
                    if (d > now.AddDays(1)) d = d.AddYears(-1);
                    return (now - d).TotalMinutes;
                }
            }
            catch { }
            return -1;
        }
    }
}
