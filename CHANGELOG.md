# Changelog

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

## [0.4.0] - 2026-10-20

### Added

- **跳到 Claude 桌面端会话**：双击会话时会按会话名选中桌面端侧边栏里的对应会话，
  直接进入该会话，而不是只把窗口带到前台。

### Changed

- **总览窗显示真实会话名**：非隐私模式下格子优先显示 Claude 记录的会话名（`name`），
  隐私模式下仍显示「会话 N」；格子加宽以容纳更长的名字。单击只选中，双击直接跳到对应会话，
  不再打开主窗口。

### Fixed

- **Codex 会话名**：标题查找改用文件名里的会话 ID，老会话不再退回显示 `Codex · <id>`，
  总览与列表显示 Codex 记录的 `thread_name`。

## [0.3.0] - 2026-10-20

### Added

- **多来源监控**：Claude Code / Codex CLI / DeepSeek Harness 的会话同屏显示。
- 每行带来源徽标（CLD / CDX / DSH），总览窗带来源色条。
- 设置里可分别关闭某个来源（监控来源：Claude Code / Codex / DeepSeek Harness）。
- **Codex**：读 `~/.codex/sessions/**/rollout-*.jsonl` 与 `session_index.jsonl`，
  识别 `task_started` / `task_complete` / `exec_approval_request` / `turn_aborted` / `error`。
- **DeepSeek Harness**：读 `~/.dsh/storages/session_projcache/sessions/*.json`，
  识别 `inbox`（等你输入）/ `openStep`（工作中）/ `goal.failure`（出错），
  并从 `turnOutline` 取最后一轮提问与回复。
- 隐私模式别名带来源：`Claude 1` / `Codex 1` / `DSH 1`。
- 界面显示名改为「AI 状态红绿灯」（安装包与程序标识保持 `ClaudeTrafficLight` 不变）。

### Notes

- Codex 的压缩 rollout（`.jsonl.zst`）不解析；只监控活/近期未压缩的会话。
- DeepSeek Harness 只在桌面端运行时显示其会话；关闭后按「已结束」处理（可在设置里显示）。
- 跳转：Claude Code 精确选中会话标签；Codex / DSH 目前激活宿主窗口。

## [0.2.1] - 2026-10-20

### Fixed

- **跳转到会话**：VSCode 里的会话现在会直接选中/打开对应的**会话标签页**，而不是只把
  VSCode 窗口带到前台。优先用 UI Automation 按会话标题选中已经打开的标签；标签没开时，
  用扩展的 `vscode://anthropic.claude-code/open?session=<id>` 打开后再选中。
  终端里的会话仍然只激活宿主窗口。

## [0.2.0] - 2026-10-20

### Added

- **安装包**：首次安装选择目录、覆盖升级（保留设置 / 钩子 / 自启）、静默升级、
  卸载时清理程序目录 / 注册表 / 快捷方式 / 本程序安装的钩子、控制面板卸载项。
- **软件内更新**：更新源配置（`update.json`）、清单（`latest.json`）、下载进度、
  SHA256 校验、调用安装包静默覆盖并重启。
- **隐私模式**：列表只显示「会话 N」，隐藏标题 / 项目名 / 提问 / 回复 / 目录，
  通知与总览同步脱敏；设置与托盘双入口。
- 托盘菜单增加隐私模式开关；安装器支持开机自启，应用启动时按注册表校准设置。
- GitHub Actions：推送构建；`v*` 标签自动构建并发布 Release（含安装包与 `latest.json`）。

### Changed

- 版本号升至 0.2.0。
- 默认更新源指向本仓库 Release 的 `latest.json`（可用 `update.json` 覆盖）。

### Fixed

- 同一版本的不同写法（`0.2.0.0` vs `0.2.0`）不再误报更新。
- 安装 / 升级时会停掉所有正在运行的旧副本，避免单实例锁导致「安装完成后运行」只是唤起旧窗口。
- 标题栏在窄窗口下不再挤掉会话统计。

## [0.1.0] - 2026-10-20

### Added

- 首个可运行版本：置顶三色灯、会话列表与详情、总览窗、托盘气泡通知、
  未读标记、状态排序、窗口拖动与缩放、设置持久化、开机自启、
  Claude 生命周期钩子安装 / 卸载、运行日志。
