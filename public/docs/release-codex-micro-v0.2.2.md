# Codex Micro Keypad v0.2.2

This release adds an at-a-glance remaining-quota gauge to the lower-left settings knob.

## What's new

- The black settings knob now shows the remaining Codex quota as a percentage and progress ring.
- When multiple quota windows are available, the gauge shows the tighter window; the tooltip lists every window and its local reset time.
- The ring uses blue, amber, and red states so low remaining quota is easy to recognize without opening Codex.
- Quota refreshes every two minutes while the keypad is visible and pauses while hidden.
- A failed read is shown as unknown instead of `0%`; after a successful read, a later failure keeps the last known value and labels it as stale in the tooltip.
- Chinese and English captions, tooltips, and accessibility text are included.

## Download and run

- `CodexMicro-Keypad-0.2.2-win-x64.zip`: standalone keypad for Windows x64.
- `CodexMicro-Keypad-0.2.2-win-x64.zip.sha256`: checksum for the keypad archive.
- Extract the complete archive, then run `CodexMicro.exe`.
- This compact package is framework-dependent and requires the [official Microsoft .NET 10 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

When upgrading, exit the previous version from its tray menu before replacing it or extracting the new version into a separate directory.

## Driver notice — unchanged

The included `CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER.zip` and checksum are unchanged from v0.2.1. The driver remains an unsigned developer build. Review its source first, then sign and install it locally using your own trusted certificate. Do not disable Windows driver-signature enforcement, and do not install untrusted certificates or drivers.

## Verification

- Codex Micro desktop tests: 89 passed.
- Full solution tests: 928 passed.
- Standalone Release build: 0 warnings, 0 errors.

---

# Codex Micro Keypad v0.2.2（简体中文）

本版本在左下角黑色设置旋钮中加入一眼可见的 Codex 剩余额度仪表。

## 主要更新

- 黑色设置旋钮现在显示 Codex 剩余额度百分比和环形进度。
- 同时存在多个额度窗口时，仪表显示更紧张的一档；悬停提示会列出所有窗口及本地重置时间。
- 环形进度采用蓝、黄、红三档状态，不打开 Codex 也能快速识别低额度。
- 小键盘显示时每两分钟刷新额度，隐藏后暂停刷新。
- 读取失败会显示未知状态而不是误报 `0%`；已有成功结果时，后续刷新失败会保留上次结果，并在提示中标记。
- 完整支持中英文标题、提示和无障碍文本。

## 下载与运行

- `CodexMicro-Keypad-0.2.2-win-x64.zip`：独立小键盘程序（Windows x64）。
- `CodexMicro-Keypad-0.2.2-win-x64.zip.sha256`：小键盘压缩包校验值。
- 解压完整目录后运行 `CodexMicro.exe`。
- 本包为精简的 framework-dependent 构建，需要安装[微软官方 .NET 10 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)。

升级时，请先从旧版托盘菜单退出，再覆盖或解压到新目录。

## 驱动说明（未变更）

附带的 `CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER.zip` 及校验文件与 v0.2.1 相同。当前驱动仍是未签名的开发者构建。请先审查源码，再使用自己的受信任证书完成本地签名和安装。不要关闭 Windows 驱动签名强制，也不要安装来源不明的证书或驱动。

## 验证

- Codex Micro 桌面测试：89 项通过。
- 完整解决方案测试：928 项通过。
- 独立 Release 构建：0 警告、0 错误。
