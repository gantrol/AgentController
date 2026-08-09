# Agent Controller v1.2.0

Agent Controller 1.2 separates the Codex Micro keypad from the main application and adds a compact Windows package alongside the existing self-contained package.

## Downloads

- `AgentController-1.2.0-win-x64.zip`: self-contained Windows x64 package. No separate .NET installation is required.
- `AgentController-1.2.0-win-x64-compact.zip`: compact, framework-dependent Windows x64 package. It requires the [official Microsoft .NET 10 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
- Each archive has a matching `.sha256` checksum file.
- The standalone keypad is published separately as [Codex Micro Keypad v1.2.0](https://github.com/gantrol/AgentController/releases/tag/codex-micro-v1.2.0).

Extract exactly one Controller archive completely, then run `AgentController.exe` as a normal user. Do not combine the self-contained and compact archives in the same folder.

## Highlights

- Moved the Codex Micro keypad into its own lightweight `CodexMicro.exe`; Agent Controller now launches the external keypad instead of hosting a second copy of its UI.
- Preserved controller and keypad coexistence through separate logical leases on one current-user Micro Broker.
- Added a reproducible `-Compact` packaging mode with a size limit and SHA-256 generation while retaining the original self-contained package.
- Kept the Controller interface, controller mappings, localization, startup option, and safety behavior unchanged.

## Driver notice

Device Support remains an optional unsigned developer component and is unchanged from the previous Controller release. Review the source before locally signing and installing it with your own trusted certificate. Do not disable Windows driver-signature enforcement, and do not install untrusted certificates or drivers.

## Validation

- All 917 tests in the Release solution pass, plus all 5 standalone Codex Micro Protocol tests.
- Both Controller archives are built from the same tagged source and receive independent SHA-256 checksums.
- The compact archive is approximately 9.60 MiB, compared with approximately 75.61 MiB for the self-contained archive.
- The compact archive starts a responsive `AgentController.exe` window with file version `1.2.0.0` on a machine with .NET 10 Desktop Runtime x64 installed.

## Known limits

- Codex integration follows observed private contracts that may change in future Codex releases.
- The application and developer driver are unsigned. Review the source and test with non-critical tasks first.
- The compact package will not start until the required Desktop Runtime is installed; use the self-contained package if you do not want that dependency.

---

# Agent Controller v1.2.0（简体中文）

Agent Controller 1.2 将 Codex Micro 小键盘从主程序中独立出来，并在原有自包含包之外新增 Windows 精简包。

## 下载

- `AgentController-1.2.0-win-x64.zip`：Windows x64 自包含包，不需要另装 .NET。
- `AgentController-1.2.0-win-x64-compact.zip`：Windows x64 framework-dependent 精简包，需要安装[微软官方 .NET 10 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)。
- 每个压缩包都有对应的 `.sha256` 校验文件。
- 独立小键盘在 [Codex Micro Keypad v1.2.0](https://github.com/gantrol/AgentController/releases/tag/codex-micro-v1.2.0) 单独发布。

请任选一种 Controller 压缩包完整解压，再以普通用户运行 `AgentController.exe`。不要把自包含包与精简包解压到同一个目录混用。

## 主要更新

- 将 Codex Micro 小键盘独立为轻量的 `CodexMicro.exe`；Agent Controller 现在启动外部小键盘，不再内嵌另一份界面。
- 小键盘与手柄输入使用各自的逻辑租约，并共享当前用户唯一的 Micro Broker，两者可以并存。
- 新增可复现的 `-Compact` 封包模式，包含体积上限和 SHA-256 生成，同时保留原有自包含包。
- Controller 界面、手柄映射、语言、开机自启动和安全行为保持不变。

## 驱动说明

Device Support 仍是可选的未签名开发者组件，与上一版 Controller Release 相同。请先审查源码，再用自己的受信任证书在本机签名和安装。不要关闭 Windows 驱动签名强制，也不要安装来源不明的证书或驱动。

## 验证

- Release 解决方案 917 项测试全部通过，另有 5 项独立 Codex Micro Protocol 测试全部通过。
- 两种 Controller 压缩包都由同一个标签源码生成，并分别提供 SHA-256 校验值。
- 精简包约 9.60 MiB，自包含包约 75.61 MiB。
- 在已安装 .NET 10 Desktop Runtime x64 的机器上，精简包可启动文件版本为 `1.2.0.0`、正常响应的 `AgentController.exe` 窗口。

## 已知限制

- Codex 集成依赖观察所得的私有协议，未来 Codex 版本可能改变这些行为。
- 应用和开发者驱动均未签名。请先审查源码，并仅用非关键任务试用。
- 未安装所需 Desktop Runtime 时精简包无法启动；不希望增加该依赖时请使用自包含包。
