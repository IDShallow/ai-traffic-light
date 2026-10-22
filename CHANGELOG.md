# Changelog

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

## [0.5.7] - 2026-10-22

### Added

- **项目主页入口**：设置页显示 GitHub 地址（可点击），并附「给个 ⭐ Star / 提 Issue」的文案；
  托盘菜单也加了「GitHub 项目主页（求个 ⭐）」。

## [0.5.6] - 2026-10-22

### Added

- **选择监控的对话**：设置 →「选择监控的对话…」里勾选要监控的会话，默认全选。
  取消勾选的会话不再显示在列表 / 总览里，也不参与托盘红绿灯与提醒；新会话默认勾选。
- **界面缩放**：设置 →「界面缩放」可选「自动（跟随系统）」或 100% / 125% / 150% /
  175% / 200%，立即生效，主窗与总览按比例调整。设置窗口内容改为可滚动，屏幕放不下时
  仍能完整操作。

## [0.5.5] - 2026-10-22

### Added

- **免安装版自更新**：便携版不再调用安装器，直接下载新的 `ClaudeTrafficLight.exe` 就地替换
  并重启——不写注册表、不建快捷方式、不产生 `uninstall.exe`。`latest.json` 新增
  `exeUrl` / `exeSha256`；安装版仍走原来的安装器。
- **新版本提示**：启动后 5 秒、之后按设置的间隔检查更新（1/3/6/12/24 小时，默认 6）；
  有新版时主界面「设置」按钮右上角显示红点，设置页显示「发现新版本 vX」。
  设置里新增「自动检查更新」开关和频率下拉框（默认开）。

### Fixed

- **DeepSeek Harness 跳转**：原来只把 DSH 窗口带到前台；现在通过 UIA
  (`SelectionItemPattern`) 在左侧会话列表里选中对应会话。同名会话用
  `sessionListMetadata.lastPromptAt`（界面上「N天」的依据）区分。

## [0.5.4] - 2026-10-22

### Fixed

- **高 DPI / 4K 下字体过小**（#3）：窗口初始化时直接向 Windows 查询所在显示器的 DPI
  （`GetDpiForWindow`），不再用 `Control.DeviceDpi`——后者在 WinForms 未启用
  PerMonitorV2 时返回系统（主屏）DPI，4K 副屏会按 100% 渲染。主窗 / 总览 / 设置 /
  更新对话框都已改；启动日志新增 `main dpi init=` / `overview dpi init=`。

## [0.5.3] - 2026-10-21

### Added

- **跳转后收起主窗口**：跳到会话成功后自动把主窗口收进托盘（总览窗口保持不动）。
  设置里可关掉，默认开启；双击会话、快捷回答、托盘菜单跳转都生效。
  首次触发时托盘气泡会说明一次（只提示一次），设置项下面也有说明文字。

## [0.5.2] - 2026-10-21

### Fixed

- **每屏 DPI**：进程改为 Per-Monitor V2 感知（`app.manifest`），窗口移到缩放比例不同的
  显示器时按该屏 DPI 重算字号与布局，不再由 Windows 位图拉伸（发虚）。
  主窗口、总览、设置、更新对话框都处理 `WM_DPICHANGED`；字体改为像素单位，
  每块屏各用一档字号。
- **跨屏记忆尺寸**：主窗口保存尺寸时一并记录所在显示器的 DPI，换到不同 DPI 的屏幕
  启动时按比例换算，观感尺寸保持一致。

## [0.5.1] - 2026-10-21

### Fixed

- **多显示器适配**：总览小窗口移到副屏后，刷新时不再被拽回主屏。
  窗口位置的兜底判断改为"窗口所在的那块屏幕"——只要还有一半面积在某块屏幕上，
  就保持原位；只有显示器被拔掉、窗口完全跑出屏幕时，才夹回最近的一块。
  主窗口的启动兜底同样改为按屏幕判断（副屏、负坐标、跨屏都保持原样）。

## [0.5.0] - 2026-10-21

### Added

- **面板快捷回答**：会话等待回答时，详情面板直接列出问题与选项；点一下 =
  聚焦会话 + 通过扩展 deep link 把选项预填进输入框（发送仍由用户按 Enter）。
  Claude Code / VSCode 会话生效；隐私模式下不显示问题与选项。

### Fixed

- 待回答状态不再每 20 秒重复弹通知（没有新的 hook 事件就不重复提示）。

## [0.4.2] - 2026-10-21

### Fixed

- **Codex 活跃会话同步**：新版 Codex 桌面端改用 `state_*.sqlite` / `thread_history_*.sqlite`
  保存实时状态，`rollout-*.jsonl` 不再追加。现在直接读 SQLite 会话库补全会话、标题与
  工作/完成状态，rollout 作为兼容回退；正在对话的会话不会再漏显示。
- **Codex 请求输入提示**：对话中调用 `request_user_input[_async]` 时立即进入
  「等待你回答」并弹出问题内容；这一轮结束（`task_complete`）或用户回答后恢复，
  不会长期停在等待状态。同步版调用会一直等到拿到输出。
- 有 `rollout` 的会话仍以 rollout 的状态为准（会话库缺历史表时不会被误判成已完成）；
  SQLite 查询对 thread id 做转义；`updated_at_ms` 缺失时回退到秒级 `updated_at`。

## [0.4.1] - 2026-10-21

### Fixed

- **状态兜底**：未安装钩子时，注册表的 `status` 可能滞后（新回合开始、模型还在生成时仍是 `idle`）。
  现在解析转录的阶段（用户提问 / `tool_use` / `tool_result` / `end_turn`）：
  如果转录里已经有比 `statusUpdatedAt`（或 Stop 钩子）更新的"工作中"记录，就按工作中算。
- 没有 timestamp 的 `last-prompt` 不再把阶段时间设为"现在"，避免启动时把已完成会话误判成工作中。

## [0.4.0] - 2026-10-20

### Added

- **跳到 Claude 桌面端会话**：双击会话时会按会话名选中桌面端侧边栏里的对应会话，
  直接进入该会话，而不是只把窗口带到前台。

### Changed

- **总览窗显示真实会话名**：非隐私模式下格子优先显示会话标题；没有标题时用会话名
  （`administrator-e7` 这类派生名除外）。格子加宽以容纳更长的名字。
  单击选中并打开主窗口，双击直接跳到对应会话。

### Fixed

- **Claude Code 标题回归**：会话行仍然优先显示 AI 标题，不会被注册表里的派生名
  （`administrator-xx`）覆盖。
- **深链防护**：`codex://` / `claude://` 未注册时不再尝试启动，避免弹出"选择打开方式"。
- **Codex 会话名**：标题查找改用文件名里的会话 ID，老会话不再退回显示 `Codex · <id>`，
  总览与列表显示 Codex 记录的 `thread_name`。
- **跳到 Codex 会话**：双击 Codex 会话会通过 `codex://threads/<id>` 直接打开
  ChatGPT/Codex 桌面端里的对应会话，不再错误地切到 VS Code。

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
