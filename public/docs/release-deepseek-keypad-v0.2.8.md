# DeepSeek 键盘 0.2.8

普通用户只需下载并解压：

`Deepseek-Harness-Keypad-v0.2.8-win-x64.zip`

运行其中的 `CodexMicro.exe`。本包包含 Micro Bridge 与自动配置入口；首次配置时会
在线安装并锁定 DeepSeek Harness `v0.1.0-rc.8`，不把数百 MiB 的完整 WSL 根文件系统
塞进每位用户的下载包。Windows 需安装 .NET 10 Desktop Runtime x64，托管模式仍需
启用 WSL。

## 主要变化

- 托管 DSH 从 `rc.6` 升级到 `rc.8`；检测到旧版时先提示，再执行可回滚升级。
- 仅迁移 API 凭据、模型设置和匿名 ID；不把不兼容的旧会话数据库导入 `rc.8`。
- 登记 `deepseek-v4-flash-vision-exp` 的文本与图片输入能力，但不强制替换默认模型。
- 实机把测试 PNG 粘贴到 `rc.8` Web UI，官方模型正确读出图中的模型名和 `0.2.8`。
- 纯文本模型仍不能识图；Harness 的图片通路不会给后端模型凭空增加视觉能力。

WSL 系统功能仍由 Windows 提供。安装和升级细节见包内
`DEEPSEEK-WINDOWS-SETUP.zh-CN.md`。

## English

Users only need `Deepseek-Harness-Keypad-v0.2.8-win-x64.zip`. Extract it and
run `CodexMicro.exe`. The small online package includes the Micro Bridge and
installs the pinned DeepSeek Harness `v0.1.0-rc.8` during managed first-run
setup instead of bundling a full WSL root filesystem. It requires the .NET 10
Desktop Runtime x64 and WSL. The managed upgrade is rollback-safe and does not
import the storage-incompatible legacy session database. An end-to-end PNG
upload test with `deepseek-v4-flash-vision-exp` successfully read the image;
text-only backend models remain text-only.
