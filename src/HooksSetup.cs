using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;

namespace ClaudeTrafficLight
{
    /// <summary>往 ~/.claude/settings.json 装/卸生命周期钩子。只增不改、写前备份。</summary>
    internal static class HooksSetup
    {
        public static readonly string[] Events = new string[]
        {
            "SessionStart", "UserPromptSubmit", "Notification", "Stop", "StopFailure", "SessionEnd"
        };

        public static string HookCommand()
        {
            // 末尾的 --hook-id 是"这条是我的"的稳定标记：exe 改名/搬家也能认出来。
            // 路径没空格时用裸路径 —— cmd 和 PowerShell 都能直接跑；
            // 有空格时才用 & "..."（Claude Code 在 Windows 上是用 PowerShell 执行 hook 的）。
            string tail = " --hook --hook-id=claude-traffic-light";
            if (Paths.ExePath.IndexOf(' ') >= 0)
                return "& \"" + Paths.ExePath + "\"" + tail;
            return Paths.ExePath + tail;
        }

        public static bool LooksOurs(string command)
        {
            if (string.IsNullOrEmpty(command)) return false;
            if (command.IndexOf("--hook-id=claude-traffic-light", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return command.IndexOf("ClaudeTrafficLight", StringComparison.OrdinalIgnoreCase) >= 0
                && command.IndexOf("--hook", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Dictionary<string, object> ReadRoot(out string error)
        {
            error = "";
            try
            {
                if (!File.Exists(Paths.ClaudeSettingsFile))
                    return new Dictionary<string, object>();

                string text = File.ReadAllText(Paths.ClaudeSettingsFile);
                if (string.IsNullOrWhiteSpace(text)) return new Dictionary<string, object>();

                Dictionary<string, object> root = Json.ParseObject(text);
                if (root == null)
                {
                    error = "settings.json 不是合法 JSON，未做任何改动。";
                    return null;
                }
                return root;
            }
            catch (Exception ex)
            {
                error = "读取 settings.json 失败：" + ex.Message;
                return null;
            }
        }

        private static void Backup()
        {
            try
            {
                if (!File.Exists(Paths.ClaudeSettingsFile)) return;
                string name = "settings.json.bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(Paths.ClaudeSettingsFile, Path.Combine(Paths.ClaudeDir, name), true);
            }
            catch { }
        }

        public static bool Install(out string error)
        {
            error = "";
            Dictionary<string, object> root = ReadRoot(out error);
            if (root == null) return false;

            if (root.ContainsKey("hooks") && !(root["hooks"] is Dictionary<string, object>))
            {
                error = "settings.json 里的 \"hooks\" 不是对象，未做任何改动。";
                return false;
            }

            Dictionary<string, object> hooks = Json.Map(root, "hooks");
            if (hooks == null)
            {
                hooks = new Dictionary<string, object>();
                root["hooks"] = hooks;
            }

            for (int i = 0; i < Events.Length; i++)
            {
                string ev = Events[i];
                object[] arr = Json.List(hooks, ev);
                List<object> groups = new List<object>();
                if (arr != null) groups.AddRange(arr);

                bool found = false;
                for (int g = 0; g < groups.Count; g++)
                {
                    Dictionary<string, object> grp = groups[g] as Dictionary<string, object>;
                    if (grp == null) continue;
                    object[] hs = Json.List(grp, "hooks");
                    if (hs == null) continue;
                    for (int h = 0; h < hs.Length; h++)
                    {
                        Dictionary<string, object> hd = hs[h] as Dictionary<string, object>;
                        if (hd != null && LooksOurs(Json.Str(hd, "command")))
                        {
                            // 程序被移动/改名过：把旧命令更新成当前路径，别跳过
                            string want = HookCommand();
                            if (!string.Equals(Json.Str(hd, "command"), want, StringComparison.Ordinal))
                                hd["command"] = want;
                            found = true;
                            break;
                        }
                    }
                    if (found) break;
                }

                if (!found)
                {
                    Dictionary<string, object> entry = new Dictionary<string, object>();
                    entry["type"] = "command";
                    entry["command"] = HookCommand();
                    entry["timeout"] = 5;

                    Dictionary<string, object> grp = new Dictionary<string, object>();
                    grp["hooks"] = new object[] { entry };
                    groups.Add(grp);
                }

                hooks[ev] = groups.ToArray();
            }

            try
            {
                Backup();
                Directory.CreateDirectory(Paths.ClaudeDir);
                File.WriteAllText(Paths.ClaudeSettingsFile, Json.Pretty(root), new UTF8Encoding(false));
                Log.Write("hooks installed -> " + Paths.ClaudeSettingsFile);
                return true;
            }
            catch (Exception ex)
            {
                error = "写入 settings.json 失败：" + ex.Message;
                return false;
            }
        }

        public static bool Uninstall(out string error)
        {
            error = "";
            Dictionary<string, object> root = ReadRoot(out error);
            if (root == null) return false;

            if (root.ContainsKey("hooks") && !(root["hooks"] is Dictionary<string, object>))
                return true;

            Dictionary<string, object> hooks = Json.Map(root, "hooks");
            if (hooks == null) return true;

            for (int i = 0; i < Events.Length; i++)
            {
                string ev = Events[i];
                object[] arr = Json.List(hooks, ev);
                if (arr == null) continue;

                List<object> keep = new List<object>();
                for (int g = 0; g < arr.Length; g++)
                {
                    Dictionary<string, object> grp = arr[g] as Dictionary<string, object>;
                    if (grp == null) { keep.Add(arr[g]); continue; }

                    object[] hs = Json.List(grp, "hooks");
                    bool oursOnly = hs != null && hs.Length > 0;
                    if (hs != null)
                    {
                        for (int h = 0; h < hs.Length; h++)
                        {
                            Dictionary<string, object> hd = hs[h] as Dictionary<string, object>;
                            if (hd == null || !LooksOurs(Json.Str(hd, "command"))) { oursOnly = false; break; }
                        }
                    }
                    if (!oursOnly) keep.Add(arr[g]);
                }

                if (keep.Count == 0) hooks.Remove(ev);
                else hooks[ev] = keep.ToArray();
            }

            try
            {
                Backup();
                File.WriteAllText(Paths.ClaudeSettingsFile, Json.Pretty(root), new UTF8Encoding(false));
                Log.Write("hooks uninstalled");
                return true;
            }
            catch (Exception ex)
            {
                error = "写入 settings.json 失败：" + ex.Message;
                return false;
            }
        }

        public static bool IsInstalled()
        {
            try
            {
                if (!File.Exists(Paths.ClaudeSettingsFile)) return false;
                string text = File.ReadAllText(Paths.ClaudeSettingsFile);
                Dictionary<string, object> root = Json.ParseObject(text);
                if (root == null) return false;
                Dictionary<string, object> hooks = Json.Map(root, "hooks");
                if (hooks == null) return false;

                for (int i = 0; i < Events.Length; i++)
                {
                    object[] arr = Json.List(hooks, Events[i]);
                    if (arr == null) return false;
                    bool found = false;
                    for (int g = 0; g < arr.Length && !found; g++)
                    {
                        Dictionary<string, object> grp = arr[g] as Dictionary<string, object>;
                        if (grp == null) continue;
                        object[] hs = Json.List(grp, "hooks");
                        if (hs == null) continue;
                        for (int h = 0; h < hs.Length; h++)
                        {
                            Dictionary<string, object> hd = hs[h] as Dictionary<string, object>;
                            if (hd == null) continue;
                            string cmd = Json.Str(hd, "command");
                            if (LooksOurs(cmd) &&
                                cmd.IndexOf(Paths.ExePath, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                found = true;
                                break;
                            }
                        }
                    }
                    if (!found) return false;
                }
                return true;
            }
            catch { return false; }
        }
    }
}
