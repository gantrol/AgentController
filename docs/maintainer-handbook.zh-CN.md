# 人类开发、使用、维护与发布指南

适用对象：接手本仓库、处理故障和执行发布的维护者。以下命令从仓库根目录运行，使用 Windows PowerShell 5.1 或 PowerShell 7。Windows 主程序以 `app/AgentController.csproj` 为版本事实源。

快速入口：[选择产品](#1-先确定维护哪个产品) · [环境准备](#2-接手环境) · [开发与调试](#3-日常改动与定位) · [设置恢复](#4-设置备份与恢复) · [Windows 发布](#5-windows-主程序发布) · [失败与回退](#6-发布失败重试与回退) · [其他产品](#7-其他产品的发布入口) · [脚本速查](#8-脚本速查)。

本文中的启动、调试、验收和发布命令供人类按需执行，不会因为阅读文档而自动执行。源码路径和脚本以当前工作区为准；打包后的 `DOCS` 只包含部分文档，开发维护请使用完整仓库。

## 1. 先确定维护哪个产品

| 产品 | 代码与版本位置 | 封包／发布入口 | 产物位置 |
| --- | --- | --- | --- |
| Windows Agent Controller | `app/AgentController.csproj` | `scripts/package-release.ps1`、`scripts/publish-release.ps1` | `dist/AgentController-版本-win-x64[-compact].zip` 及 `.sha256` |
| 独立 Micro 小键盘／Monitor | `virtual-micro/src/CodexMicro.DesktopHost/`；发布版本还由脚本参数指定 | `scripts/package-micro.ps1`，选择 `standard` 或 `monitor` | `dist/` |
| DeepSeek 小键盘在线包 | 上述 Host、`micro-bridge/DeepSeekHarness/` | `scripts/package-micro.ps1 -Preset deepseek`、`scripts/publish-deepseek-release.ps1` | `dist/Deepseek-Harness-Keypad-v版本-win-x64.zip` |
| macOS Foundation Preview | `src/AgentController.Desktop/`、`src/AgentController.Platform.MacOS/` | `scripts/publish-macos.ps1` | `artifacts/macos/` 下两个架构的 `.app` |
| Windows 虚拟设备驱动 | `virtual-micro/driver/` | 按[驱动说明](../virtual-micro/UNSIGNED-DRIVER.zh-CN.md)单独处理 | 与应用 zip 分开交付 |

这些产品的版本分别维护，不能把 Windows 主程序的版本号套给所有子项目。主程序包含 DeepSeek 控制能力，并不代表发布主程序时也需要重发小键盘或驱动。

## 2. 接手环境

1. 安装 Git 和 `global.json` 要求的 .NET SDK。当前配置为 `10.0.302`，允许 `latestPatch`；以文件和 `dotnet --version` 的实际结果为准。
2. 使用 IDE 时需要能加载该 SDK 的 Visual Studio 2026 / MSBuild 18 或更新版本。
3. 上传 GitHub Release 需要 GitHub CLI，并由维护者执行 `gh auth login`。本地检查、构建、封包不需要 GitHub 登录。
4. 只有维护 DeepSeek bridge 时才需要 Node.js、pnpm；版本要求见其 `package.json` 的 `engines`、`packageManager`，安装依赖使用 `pnpm install --frozen-lockfile`。

```powershell
.\scripts\maintain.ps1
.\scripts\maintain.ps1 -Action Build
```

第一条只检查 SDK、主程序版本字段、Git 提交和工作区状态，报告 GitHub CLI 是否存在。第二条先做同样的检查，再执行 solution restore 和 Release build，任一命令失败即停止。首次 restore 需要访问 NuGet。

如本机执行策略阻止脚本，可在**单次进程**中运行，例如：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\maintain.ps1 -Action Check
```

无需修改整台机器的执行策略。脚本不要求管理员权限，驱动安装流程除外。

## 3. 日常改动与定位

### 3.1 从源码启动与日常使用

先从通知区域退出已运行的对应产品，再选择一个入口。以下命令会构建并打开真实应用：

```powershell
# Windows 手柄主程序
dotnet run --project .\app\AgentController.csproj -c Debug

# 独立 Micro 小键盘；不要把共享 WPF 类库设为启动项目
dotnet run --project .\virtual-micro\src\CodexMicro.DesktopHost\CodexMicro.DesktopHost.csproj -c Debug
```

日常使用主程序时，以 XInput 模式连接手柄，启动目标 Agent，确认桥接开启。View 切换 Codex / DeepSeek，Menu 唤醒当前目标，松开按键并让摇杆回中后再操作。完整映射见[操作清单](../public/docs/controller-operations.md)。长按 R3 打开设置；需要彻底退出时使用通知区域菜单。独立小键盘不是主程序的启动依赖，完整 Micro 输入另需 Device Support。

主程序支持 `--background`（启动后隐藏窗口）与 `--dev-instance`（绕过单实例检查并显示 Preview 标题）。例如 `dotnet run --project .\app\AgentController.csproj -c Debug -- --dev-instance`。后者**不隔离设置、手柄或目标 Agent**，不能当成沙箱；通常先退出旧实例更容易定位问题。小键盘有自己的单实例检查，不要照搬这个参数。

### 3.2 代码入口与修改范围

| 改动／故障 | 先看哪里 | 维护时需要保留的约束 |
| --- | --- | --- |
| 手柄映射、窗口、设置、本地化 | `app/`、`app/Localization/` | 以[操作清单](../public/docs/controller-operations.md)核对实际行为 |
| 共享业务与平台接口 | `src/AgentController.Application/`、`src/AgentController.Domain/`、`src/AgentController.Platform.Abstractions/` | 平台实现留在相应适配层 |
| Micro 发送、设备竞争、输入丢失 | `src/AgentController.MicroBroker/`、`virtual-micro/src/CodexMicro.Protocol/` | 核对[输入链路](../public/docs/architecture-and-input-flow.md)，区分未发送与发送结果未知 |
| Codex 更新后失效 | `app/Services/`、`docs/known-issues/` | 记录 Codex 构建号，先确定受影响动作和协议路径 |
| DeepSeek 目标失效 | `app/Agents/DeepSeek/`、`micro-bridge/DeepSeekHarness/` | 保持 Codex 与 DeepSeek 目标隔离 |
| 包内缺文件、版本错误、依赖错误 | `scripts/`、各 `.csproj`、`Directory.Packages.props` | 从同一提交封包；核对自包含与精简模式 |

每次修复记录：应用版本与提交、Windows 版本、Agent 版本、手柄型号与模式、是否安装驱动、复现步骤、预期与实际结果。优先复现一个动作，再修改对应层。不要把本机任务数据库、账号配置或整个用户目录附到 issue。

依赖版本集中在根目录及 `virtual-micro/` 内的 `Directory.Packages.props`；更新对应文件后重新 restore/build。不要顺手改动其他产品的版本或重新生成无关资源。

### 3.3 编译与现有检查

只编译正在修改的 Windows 主程序、保留调试符号：

```powershell
dotnet build .\app\AgentController.csproj -c Debug
```

定位编译错误时可保存 MSBuild 二进制日志：

```powershell
New-Item -ItemType Directory -Path .\.artifacts\maintenance -Force | Out-Null
dotnet build .\app\AgentController.csproj -c Debug -bl:.\.artifacts\maintenance\controller-debug.binlog
if ($LASTEXITCODE -ne 0) { throw 'Debug build failed; inspect controller-debug.binlog' }
```

binlog 可能包含本机路径、构建属性和环境信息，分享前先审查。依赖问题先核对 SDK、NuGet 源与第一条失败信息；文件占用先退出对应应用，不要直接清空全局缓存。改动共享层后，再用 `maintain.ps1 -Action Build` 编译整个 solution。

现有自动化检查由人类按改动范围执行；维护和封包脚本不会自动运行测试或打开应用：

```powershell
# 在 maintain.ps1 -Action Build 成功后运行现有 .NET 测试
dotnet test .\AgentController.sln -c Release --no-build
```

修改 DeepSeek bridge 时另行运行：

```powershell
Push-Location .\micro-bridge\DeepSeekHarness
try {
    pnpm install --frozen-lockfile
    if ($LASTEXITCODE -ne 0) { throw 'pnpm install failed' }
    pnpm run verify
    if ($LASTEXITCODE -ne 0) { throw 'Bridge verification failed' }
}
finally { Pop-Location }
```

`verify` 包含现有类型检查、单元测试及构建。失败时停止发布，记录原始输出；历史版本说明中的通过数量不能用作本次验证记录。

### 3.4 断点、日志与常见 debug

在 Visual Studio 中将 `app/AgentController.csproj` 或 `CodexMicro.DesktopHost.csproj` 设为启动项目，选择 Debug，再启动调试。附加现有进程时先核对可执行文件路径，确保加载的 PDB 与该二进制来自同一次构建。主程序的进程也可能承载 Broker，不能只凭进程名选择附加目标。

主程序入口为 `app/App.xaml.cs` → `app/Composition/AppComposition.cs`。手柄输入排查可依次在 `app/Services/XInputService.cs`、`app/Controllers/BridgeInputGate.cs`、`app/Controllers/ControllerInteractionCoordinator.cs`、`app/Services/Micro/MicroInputService.cs` 与 `src/AgentController.MicroBroker/MicroBrokerHost.cs` 设置断点。先查输入是否到达，再查门控、动作映射与发送结果；详细链路见[架构与输入链路](../public/docs/architecture-and-input-flow.md)。

| 现象 | 排查顺序 |
| --- | --- |
| F5 后立即退出／新改动没有出现 | 核对启动项目和进程路径；从通知区域退出旧实例再启动。小键盘在附加调试器且存在旧实例时会向 Debug 输出提示并退出。 |
| 手柄已连接但不执行动作 | 确认 XInput 模式、桥接开启、当前受控 Agent、前台窗口和回中状态，再检查 `BridgeInputGate`。 |
| 右摇杆或 R3 无效，其他动作正常 | 分别检查 Codex Micro 设备路径和 DeepSeek bridge；记录发送结果，不能把“结果未知”当成 `NotSent` 再补发。 |
| 首次配置后快捷键无效 | 检查冲突与当前目标；写入绑定后重启 Codex，再验证对应动作。 |
| 小键盘模型切换失败 | 使用 Debug 构建复现，检查下方 `model-toggle.jsonl`；同时记录 Codex 构建号、任务状态与具体动作。 |
| DeepSeek 离线或持续 `opening` | 核对 Harness、bridge 安装和受控专用窗口；普通网页标签能上报状态不代表能接收控制。见 [Bridge 说明](../micro-bridge/DeepSeekHarness/README.zh.md)。 |
| 设置重置后又出现旧值 | 主程序会在当前配置缺失时读取旧版路径；按第 4 节同时保留并处理两处配置。 |
| Codex 升级后取消行为改变 | 先对照[请求卡取消已知问题](known-issues/codex-micro-request-card-cancel.zh-CN.md)，区分运行取消与请求卡取消。 |

当前小键盘的模型切换诊断由 `CodexModelToggleDiagnostics.cs` 在 `#if DEBUG` 下实现，文件为 `%LOCALAPPDATA%\CodexMicro\logs\model-toggle.jsonl`；Release 包不能指望生成这份日志。仅当文件存在时读取末尾记录：

```powershell
$modelLog = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CodexMicro\logs\model-toggle.jsonl'
if (Test-Path -LiteralPath $modelLog) {
    Get-Content -LiteralPath $modelLog -Tail 80
}
```

诊断记录包含任务标识、模型和耗时，提交 issue 前按需要脱敏。`%LOCALAPPDATA%\OpenAI\CodexMicro\broker-v1.lock` 是 Broker 实例租约文件，不是日志；文件存在本身不表示死锁，不要在进程运行时删除它。普通启动异常优先在调试器中查看异常与调用栈，不要假设所有产品共享一个日志目录。

修复闭环：保留最小复现及环境信息 → 找到失败层 → 修改并编译 → 由维护者选择现有检查及相关实际操作验收 → 更新受影响文档 → 审阅 `git diff --check` 和变更内容。涉及 UI 文案时遵循 `app/Localization/README.md`；产品行为以源码和本次验证为准，`todo/` 中的计划不代表已实现。

## 4. 设置备份与恢复

升级或排查设置问题前，先从通知区域退出 Agent Controller 和独立小键盘，再运行：

```powershell
.\scripts\maintain.ps1 -Action BackupSettings -WhatIf
.\scripts\maintain.ps1 -Action BackupSettings
```

备份输出到 `.artifacts/maintenance/backups/时间-随机标识/`，保留以下相对于 `%LOCALAPPDATA%` 的路径，并生成带 SHA-256 的 `manifest.json`：

- `AgentController/settings.json`：当前主程序设置。
- `CodexController/settings.json`：旧版设置；主程序在当前设置不存在时会读取它。
- `CodexMicro/settings.json`、`CodexMicro/micro-profile.json`：小键盘设置。
- `CodexMicro/keypads/` 下以 32 位 GUID 命名的 JSON：各个小键盘配置。

不存在的文件会跳过；没有任何设置时不会生成空备份。备份不会复制 Harness 连接配置、语音模型、日志或 `.codex` 任务与认证数据，也不会停止任何进程。它是应用偏好设置备份，不是完整系统备份。备份留在本地，不随 Release 上传。

恢复时保持应用退出，先为当前设置再做一份备份，然后选择需要恢复的文件。以下示例仅恢复主程序设置，将 `$backupRoot` 改成脚本输出的真实目录：

```powershell
$backupRoot = 'D:\AgentController\.artifacts\maintenance\backups\替换为实际目录'
$relative = 'AgentController\settings.json'
$records = Get-Content -LiteralPath (Join-Path $backupRoot 'manifest.json') -Raw | ConvertFrom-Json
$record = @($records | Where-Object { $_.Path -eq $relative })
if ($record.Count -ne 1) { throw 'Backup entry missing or duplicated' }
$savedFile = Join-Path $backupRoot $relative
if ((Get-FileHash -LiteralPath $savedFile -Algorithm SHA256).Hash -ine $record[0].SHA256) {
    throw 'Backup checksum mismatch'
}
$settingsFile = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) $relative
New-Item -ItemType Directory -Path (Split-Path -Parent $settingsFile) -Force | Out-Null
Copy-Item -LiteralPath $savedFile -Destination $settingsFile -Force
```

回退应用版本时优先使用升级前的对应设置备份，旧版本可能不支持新的设置格式。复制 JSON 不会同步开机自启注册项；启动应用后在设置中核对并保存一次开机自启选项。需要重置配置时，将当前和旧版设置分别改名保存，避免意外从旧路径重新导入。

## 5. Windows 主程序发布

### 5.1 决定版本，准备提交

例如计划发布 `1.2.2`（仅示例，执行时选用实际新版本）：

1. 在 `app/AgentController.csproj` 同步 `Version`、`InformationalVersion` 为 `1.2.2`，`AssemblyVersion`、`FileVersion` 为 `1.2.2.0`。预发布如 `1.2.2-rc.1` 的后两项仍用 `1.2.2.0`。
2. 更新两份根 README 的版本徽章、当前下载和构建示例、当前版本说明链接；保留历史文档原样。
3. 新建 `public/docs/release-v1.2.2.md`，写明改动、包名、运行要求、兼容性、已知问题和本次实际验证结果。未完成的人类验收明确标注待做，不复制上一版的“全部通过”。
4. 完成构建、所需现有自动化检查和源码审查，提交改动。

```powershell
.\scripts\verify-release.ps1 -SourceOnly
git diff --check
git status --short
# 审阅并提交本次改动后
.\scripts\maintain.ps1 -RequireClean
```

`verify-release.ps1` 默认从项目读取版本，检查四个版本字段、两份 README 徽章和对应的非空版本说明。`-SourceOnly` 不要求 zip 已存在。`-RequireClean` 会拒绝尚未提交的修改和未忽略的新文件。

### 5.2 从最终提交封包

```powershell
$version = ([xml](Get-Content -LiteralPath .\app\AgentController.csproj -Raw)).Project.PropertyGroup.Version
$tag = "v$version"
.\scripts\verify-release.ps1 -Version $version -SourceOnly -RequireClean
.\scripts\package-release.ps1 -Version $version
.\scripts\package-release.ps1 -Version $version -Compact
.\scripts\verify-release.ps1 -Version $version -IncludeCompact -RequireClean
```

每条命令成功后再执行下一条。第一种包自包含 .NET；`-compact` 依赖 .NET 10 Desktop Runtime for Windows x64，封包默认上限 30 MiB。两种包都从 Windows 主程序发布，输出以下四个文件：

```text
dist/AgentController-版本-win-x64.zip
dist/AgentController-版本-win-x64.zip.sha256
dist/AgentController-版本-win-x64-compact.zip
dist/AgentController-版本-win-x64-compact.zip.sha256
```

封包会重建对应 `.artifacts/release/版本/包名/` 工作目录并替换同名本地产物。需要保留的旧包应先另存。校验脚本只读检查 zip 的 SHA-256、校验文件中的文件名、可执行文件、许可证、署名、README、版本说明以及精简包大小，不解压或运行程序。自定义放宽封包大小后，发布检查仍按 30 MiB 拦截。

SHA-256 说明文件是否匹配，不证明二进制来自哪个提交，也不替代应用签名。人类必须从最终干净提交封包，并记录提交号和最终四个文件的校验结果；复用 `-SkipBuild` 前重新核对这份记录。

### 5.3 人类验收与留档

分别将自包含包和精简包解压到全新的不同目录，由维护者检查：

- [ ] 普通用户启动、通知区域退出、再次启动，显示版本正确。
- [ ] 自包含包在无对应 Runtime 的环境启动；精简包在装有对应 Desktop Runtime 的环境启动。
- [ ] 手柄连接、断开、重新连接和回中；关闭桥接、切换前台后输入边界符合预期。
- [ ] Codex / DeepSeek 切换、任务导航、旋钮、发送和取消；使用可丢弃的任务验证有副作用的动作。
- [ ] 按住说话和松开结束；未提供麦克风的环境记录为未覆盖。
- [ ] 安装驱动与未安装驱动的行为分别记录，不把有限回退写成完整 Micro 支持。
- [ ] 中英文显示、设置保存、升级前设置恢复符合预期。

留档最少包含：版本、Git SHA、操作者、日期、系统／Agent／设备版本、通过和未覆盖项目、zip 的 SHA-256、已知问题、Release URL。把记录保存在团队的发布记录位置，实际结果写入版本说明。若验收导致源码或版本说明更改，重新提交、封包并校验后再继续。

### 5.4 推送提交与标签，先上传草稿

确认当前分支正确，先推送最终提交，再创建并推送新标签。已有标签时先查明它对应的提交，不要强推或移动公开标签。

```powershell
git push
git tag -a $tag -m "Agent Controller $tag"
git push origin "refs/tags/$tag"
```

预览现有文件的上传计划，不访问远程、不构建、不修改 Release：

```powershell
.\scripts\publish-release.ps1 -Version $version -Repository gantrol/AgentController `
    -IncludeCompact -SkipBuild -Draft -WhatIf
```

确认列表和发布说明后，去掉 `-WhatIf` 创建草稿：

```powershell
.\scripts\publish-release.ps1 -Version $version -Repository gantrol/AgentController `
    -IncludeCompact -SkipBuild -Draft
```

上传时脚本要求工作区干净，并检查**目标 GitHub 仓库**的远程标签最终指向当前 HEAD，支持附注标签和轻量标签。`-WhatIf` 仅预览本地计划，不证明登录、远程标签或权限有效。指定 fork 时，标签也必须推到该 fork。

不带 `-SkipBuild` 会重新封包；正式上传已验收的产物时使用 `-SkipBuild`。默认发布说明随版本选择，也可用 `-NotesFile` 指定相对于仓库的文件；标准版本说明仍须存在，因为它会随 zip 分发。主程序只接受 `v版本` 标签和 `win-x64`。

### 5.5 公布与复核

在 GitHub 草稿中检查标题、说明和四个附件；下载草稿中的 zip 与 `.sha256`，重新计算哈希并与发布记录比对。然后由维护者执行：

```powershell
gh release edit $tag --repo gantrol/AgentController --draft=false --latest
gh release view $tag --repo gantrol/AgentController --json url,tagName,isDraft,isPrerelease,assets
```

预发布上传时加 `-Prerelease`，公布时使用 `--draft=false --prerelease --latest=false`。脚本默认不重置已有 Release 的 draft/prerelease 状态；不要依赖再次运行上传脚本将草稿转成公开版本。

## 6. 发布失败、重试与回退

| 现象 | 处理 |
| --- | --- |
| SDK 找不到或项目无法加载 | 在根目录运行 `dotnet --list-sdks`，核对 `global.json` 与 IDE 版本 |
| 四个版本字段、徽章或版本说明不一致 | 修正源文件、提交，再封包；只改 zip 文件名不能修复内部版本 |
| SHA-256 或包内容检查失败 | 保留失败输出，从目标提交重新封包；不要只重写校验值掩盖来源不明的文件 |
| 构建提示文件被占用 | 人工退出应用／小键盘后重试；脚本不会强制结束进程 |
| 工作区不干净或远程标签不是 HEAD | 检查 `git status`、`git log -1` 和标签；从正确提交重新封包 |
| GitHub 上传因同名附件失败 | 默认不覆盖。同一草稿的可追溯重试可加 `-ReplaceAssets`，它会替换本次列出的同名文件 |
| 上传部分成功、说明更新失败 | 查看草稿附件和说明，按相同版本、提交、校验值重试；不要假设发布具有事务性 |

已公开版本有功能问题时：先在版本说明记录问题和受影响范围，将上一稳定版本设回 Latest，并发布新的修复版本。不要删除或移动旧标签，也不要用相同版本号静默替换已公开二进制。

```powershell
$previousTag = 'v1.2.0' # 替换为已核验的上一稳定版本
gh release edit $previousTag --repo gantrol/AgentController --latest
```

用户端回退：退出程序，保留问题版本目录，重新下载旧版完整包到另一个目录，再按第 4 节恢复对应设置。应用回退不需要顺带卸载驱动或清空 Codex 用户数据。

## 7. 其他产品的发布入口

小键盘脚本仍有独立默认版本；调用时总是显式指定版本。核对 Host 的程序集版本、所选 preset 的说明和实际附件名。它们没有接入主程序新增的版本一致性及包内容校验。

```powershell
# 示例版本取自当前 Micro Monitor 发布
.\scripts\package-micro.ps1 -Version 0.3.5 -Preset standard
.\scripts\package-micro.ps1 -Version 0.3.5 -Preset monitor
.\scripts\package-micro.ps1 -Version 0.2.9 -Preset deepseek
```

standard 与 monitor 没有专用上传脚本，人工用 GitHub CLI 建立各自的草稿并显式列出附件；不要使用主程序的 `publish-release.ps1`，也不要使用 `dist/*` 批量上传。

DeepSeek 在线发布使用 `publish-deepseek-release.ps1`，先读取它的参数并准备对应 `release-deepseek-keypad-v版本.md`、远程标签，再用 `-SkipBuild -Draft -WhatIf` 预览。其当前默认标签为 `codex-micro-v版本`，只上传用户在线 zip：已有 Release 若包含其他附件会被拒绝，同名 zip 会直接覆盖；省略 `-Draft` 或 `-Prerelease` 还会清除对应状态。它检查的是 `origin` 标签存在性，没有主程序的目标仓库标签与 HEAD 比对。执行前审查脚本，不套用第 5 节的覆盖策略。完整 WSL payload、一键 EXE 属于另一路径，分别见 `build-deepseek-full-payload.ps1`、`package-deepseek-oneclick.ps1`，不得混入在线版附件。

macOS 脚本的 `publish` 指本地生成 `.app`，不会上传 GitHub。它使用 `--no-restore`，所以必须先还原；当前 bundle 版本在脚本中另设，更新时与 Desktop 项目一并核对：

```powershell
dotnet restore .\AgentController.sln
if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
.\scripts\publish-macos.ps1
```

后续 Mac 真机验收、签名公证及分发要求见 [macOS Foundation Preview](macos-foundation-preview.zh-CN.md)。Windows 交叉构建结果只表明本地包已生成。

## 8. 脚本速查

| 命令 | 用途 | 写入／外部动作 |
| --- | --- | --- |
| `maintain.ps1` | 环境、版本、Git 状态检查 | 无发布动作 |
| `maintain.ps1 -Action Build` | 还原和 Release 编译 | NuGet 缓存、构建目录；可能访问 NuGet |
| `maintain.ps1 -Action BackupSettings` | 备份已知应用设置 | `.artifacts/maintenance/backups/`；支持 `-WhatIf` |
| `verify-release.ps1 -SourceOnly` | 封包前检查版本与说明 | 只读 |
| `verify-release.ps1 -IncludeCompact` | 两种 zip 的哈希、内容与大小检查 | 只读 |
| `package-release.ps1 [-Compact]` | 生成一种 Windows zip 和 SHA-256 | 覆盖对应本地工作目录及同名产物 |
| `publish-release.ps1 -IncludeCompact -SkipBuild -Draft` | 上传已验收包为草稿 | GitHub 写入；预览须同时加 `-Repository owner/name -WhatIf` |

脚本抛出错误时停止本阶段；命令行进程返回非零表示失败。自动化包装这些命令时必须检查退出码。`dist/`、`.artifacts/` 和 `artifacts/` 均为本地输出，不要提交进 Git，也不要在其中存放唯一一份需要长期保留的发布记录或设置备份。
