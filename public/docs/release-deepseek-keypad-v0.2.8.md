# DeepSeek 键盘 0.2.8

普通用户只需下载并运行：

`DeepSeek-Keypad-Setup-0.2.8.exe`

本版内置 DeepSeek Harness `v0.1.0-rc.8`、Windows .NET 运行时、专用 WSL 载荷与
Micro Bridge，不需要在多个安装包之间选择。

## 主要变化

- 托管 DSH 从 `rc.6` 升级到 `rc.8`；检测到旧版时先提示，再执行可回滚升级。
- 仅迁移 API 凭据、模型设置和匿名 ID；不把不兼容的旧会话数据库导入 `rc.8`。
- 登记 `deepseek-v4-flash-vision-exp` 的文本与图片输入能力，但不强制替换默认模型。
- 实机把测试 PNG 粘贴到 `rc.8` Web UI，官方模型正确读出图中的模型名和 `0.2.8`。
- 纯文本模型仍不能识图；Harness 的图片通路不会给后端模型凭空增加视觉能力。

WSL 系统功能仍由 Windows 提供。安装和升级细节见包内
`DEEPSEEK-WINDOWS-SETUP.zh-CN.md`。

## English

Users only need `DeepSeek-Keypad-Setup-0.2.8.exe`. It bundles the Windows
.NET runtime, a clean WSL payload with DeepSeek Harness `v0.1.0-rc.8`, and the
Micro Bridge. The managed upgrade is rollback-safe and does not import the
storage-incompatible legacy session database. An end-to-end PNG upload test
with `deepseek-v4-flash-vision-exp` successfully read the image contents;
text-only backend models remain text-only.
