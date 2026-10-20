using System;

namespace ClaudeTrafficLight
{
    /// <summary>产品级常量：应用和安装包共用（安装包编译时也会带上这个文件）。</summary>
    internal static class AppInfo
    {
        public const string Version = "0.2.1";
        public const string ProductName = "Claude 状态红绿灯";
        public const string ProductId = "ClaudeTrafficLight";
        public const string ExeName = "ClaudeTrafficLight.exe";
        public const string SetupName = "ClaudeTrafficLightSetup.exe";
        public const string UninstallName = "uninstall.exe";
        public const string RunValueName = "ClaudeTrafficLight";

        /// <summary>官方发布版的默认更新源（GitHub Release 的 latest.json 资产）。</summary>
        public const string DefaultFeedUrl =
            "https://github.com/IDShallow/claude-traffic-light/releases/latest/download/latest.json";
    }
}
