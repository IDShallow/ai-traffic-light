# Contributing

感谢你愿意改进 AI Traffic Light。这个项目刻意保持**很小**：单 exe、零第三方依赖、
只做「看清本机 Claude 会话状态」这一件事。

## 构建

环境：Windows 10 / 11 + .NET Framework 4.x（系统自带 `csc.exe`）。

```cmd
tools\build.cmd
```

成功会输出 `BUILD OK`，产物在 `dist\`。不需要 NuGet，不需要 Visual Studio。

## 代码约束

- **C# 5 语法**：编译器是 .NET Framework 自带的 `csc.exe`。不要用字符串插值 `$""`、
  `?.`、`nameof`、表达式体成员、模式匹配、`out var`。
- **零第三方依赖**：只用 .NET Framework 自带程序集。
- **无遥测 / 无账号 / 无云端**：唯一的网络请求是用户主动触发的更新检查。
- **只读 Claude 数据**：`~/.claude/` 下的会话注册表与记录文件只读。
  钩子是唯一会写入 `~/.claude/settings.json` 的地方，必须**先备份、只增不改、可卸载**。
- **隐私默认优先**：新增展示内容时，考虑它在隐私模式下应该如何隐藏。
- 界面文案保持中文（与现有 UI 一致）。

## 提交 PR 前

1. `tools\build.cmd` 通过；
2. 启动 `dist\ClaudeTrafficLight.exe`，至少手动确认：列表 / 展开详情 / 总览窗 / 隐私模式；
3. 改了安装器的话，验证一次静默安装与卸载：
   ```cmd
   ClaudeTrafficLightSetup.exe --silent --dir=%TEMP%\ctl-test --no-desktop --no-autostart
   %TEMP%\ctl-test\uninstall.exe --uninstall --silent
   ```
4. 不要提交 `dist/`、日志、个人路径或令牌。

## 提交信息

用一句话说清「改了什么行为」，例如：

```
fix: 同版本号不再误报更新
feat: 隐私模式隐藏工作目录
```

## 发版

1. 改三处版本号：`src/AppInfo.cs`、`src/AssemblyInfo.cs`、`setup/SetupInfo.cs`；
2. 更新 `CHANGELOG.md`；
3. `tools\build.cmd` 确认 `BUILD OK`；
4. 提交后打标签：`git tag -a vX.Y.Z -m "vX.Y.Z"`；
5. 推送 `main` 和标签。`release` 工作流会自动构建、生成 `latest.json` 并发布 Release。

## License

提交贡献即表示同意以 [GPL-3.0](LICENSE) 授权。
