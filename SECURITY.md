# Security Policy

## 报告问题

请使用 GitHub 的 **Private vulnerability reporting / Security advisory**
（仓库 → Security → Report a vulnerability）报告安全问题，不要在公开 Issue 里贴利用细节。

## 这个程序会接触什么

- 只读：`%USERPROFILE%\.claude\sessions\` 与 `%USERPROFILE%\.claude\projects\` 下的会话状态文件。
- 可选写入：`~/.claude/settings.json`（仅在用户点「安装钩子」时；写前备份、只增不改、可一键卸载）。
- 本机配置：`%APPDATA%\ClaudeTrafficLight`（设置、日志）与 `%LOCALAPPDATA%\ClaudeTrafficLight`（日志、钩子事件）。
- 安装器：`HKCU` 下的安装信息 / 卸载信息 / 运行项，桌面与开始菜单快捷方式。

## 设计上的安全边界

- **无遥测、无账号、无云端**；不会把会话内容发送到任何地方。
- 唯一的网络请求是用户主动点「检查更新」时拉取更新清单与安装包。
- 更新安装包建议在清单里提供 `sha256`；程序会在安装前校验，不匹配直接丢弃。
- 默认按当前用户安装（`%LOCALAPPDATA%`），不需要管理员权限；装到 `Program Files`
  才会触发 UAC。
- 隐私模式是显示层开关，不改变本地读取行为。

## 支持范围

只对最新 Release 提供安全修复。
