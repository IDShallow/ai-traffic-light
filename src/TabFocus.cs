using System;
using System.Windows.Automation;

namespace ClaudeTrafficLight
{
    /// <summary>
    /// 在宿主窗口（VSCode）里按标签标题选中某个会话。
    /// VSCode 把每个 Claude 会话显示为一个以会话标题命名的编辑器标签页。
    /// </summary>
    internal static class TabFocus
    {
        public static bool SelectByTitle(IntPtr hwnd, string title)
        {
            if (hwnd == IntPtr.Zero || string.IsNullOrEmpty(title)) return false;
            try
            {
                AutomationElement root = AutomationElement.FromHandle(hwnd);
                if (root == null) return false;

                Condition cond = new PropertyCondition(
                    AutomationElement.ControlTypeProperty, ControlType.TabItem);
                AutomationElementCollection tabs = root.FindAll(TreeScope.Descendants, cond);
                if (tabs == null || tabs.Count == 0) return false;

                AutomationElement exact = null;
                AutomationElement partial = null;
                for (int i = 0; i < tabs.Count; i++)
                {
                    string name = tabs[i].Current.Name;
                    if (string.IsNullOrEmpty(name)) continue;
                    if (string.Equals(name, title, StringComparison.Ordinal))
                    {
                        exact = tabs[i];
                        break;
                    }
                    if (partial == null && name.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0)
                        partial = tabs[i];
                }

                AutomationElement target = exact != null ? exact : partial;
                if (target == null) return false;

                object pattern;
                if (!target.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
                    return false;

                ((SelectionItemPattern)pattern).Select();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
