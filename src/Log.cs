using System;
using System.IO;
using System.Text;

namespace ClaudeTrafficLight
{
    /// <summary>很小的运行日志：状态跳变 / 通知 / 钩子安装。排障用，超过 512KB 就重建。</summary>
    internal static class Log
    {
        private static readonly object Sync = new object();

        public static string FilePath
        {
            get { return Path.Combine(Paths.LocalData, "log.txt"); }
        }

        public static void Write(string message)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(Paths.LocalData);
                    FileInfo fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 512 * 1024) File.Delete(FilePath);
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + "\r\n";
                    File.AppendAllText(FilePath, line, new UTF8Encoding(false));
                }
            }
            catch
            {
                // 日志失败不影响主流程
            }
        }
    }
}
