using System;
using System.Text;
using System.Windows.Automation;

namespace ClaudeTrafficLight
{
    /// <summary>
    /// 在 Claude 桌面端主窗口里按会话名点选侧边栏条目。
    /// 桌面端把每个会话渲染成名为「状态 会话名」的按钮，Invoke 它就等于点一下。
    /// 注意：内容在 Chromium 的渲染子窗口里，主窗口本身的 UIA 树只有窗口按钮。
    /// </summary>
    internal static class DesktopFocus
    {
        private static readonly string[] StatusPrefixes = new string[]
        {
            "Running ", "Idle ", "Completed ", "Failed ", "Error ",
            "Needs input ", "Waiting ", "Working ", "Starting ",
            "Stopped ", "Offline ", "Paused ", "Done "
        };

        public static bool SelectSession(IntPtr mainWindow, string sessionName)
        {
            if (mainWindow == IntPtr.Zero || string.IsNullOrEmpty(sessionName)) return false;
            try
            {
                IntPtr renderer = FindRenderer(mainWindow);
                if (renderer == IntPtr.Zero) return false;

                AutomationElement root = AutomationElement.FromHandle(renderer);
                if (root == null) return false;

                Condition cond = new PropertyCondition(
                    AutomationElement.ControlTypeProperty, ControlType.Button);
                AutomationElementCollection buttons = root.FindAll(TreeScope.Descendants, cond);
                if (buttons == null) return false;

                AutomationElement target = null;
                for (int i = 0; i < buttons.Count && target == null; i++)
                {
                    string name = buttons[i].Current.Name;
                    if (string.IsNullOrEmpty(name)) continue;

                    for (int p = 0; p < StatusPrefixes.Length; p++)
                    {
                        string prefix = StatusPrefixes[p];
                        if (name.Length > prefix.Length &&
                            name.StartsWith(prefix, StringComparison.Ordinal) &&
                            string.Equals(name.Substring(prefix.Length), sessionName, StringComparison.Ordinal))
                        {
                            target = buttons[i];
                            break;
                        }
                    }
                }
                if (target == null) return false;

                object pattern;
                if (!target.TryGetCurrentPattern(InvokePattern.Pattern, out pattern)) return false;
                ((InvokePattern)pattern).Invoke();
                Log.Write("desktop focus invoked " + target.Current.Name);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static IntPtr FindRenderer(IntPtr mainWindow)
        {
            IntPtr found = IntPtr.Zero;
            try
            {
                Win32.EnumChildWindows(mainWindow, delegate(IntPtr h, IntPtr l)
                {
                    StringBuilder sb = new StringBuilder(256);
                    Win32.GetClassName(h, sb, sb.Capacity);
                    if (string.Equals(sb.ToString(), "Chrome_RenderWidgetHostHWND", StringComparison.Ordinal))
                    {
                        found = h;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return found;
        }
    }
}
