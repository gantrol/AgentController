# 普通用户从零安装路线

核对日期：2026-10-04。范围：Windows x64、Codex 桌面版、下载安装与首次使用；不包含源码构建和 DeepSeek Harness 环境部署。

本文区分公开发行包与当前开发中的免驱版本。安装步骤依据发行附件、打包脚本和启动代码核对；首次使用的检查点是验收要求，本次没有执行全新系统安装或真实界面验收。

## 先选择产品和版本

| 用户需要 | 对应产品 | 普通用户需要安装什么 |
| --- | --- | --- |
| 在桌面查看额度、任务状态并操作小键盘 | Codex Micro Monitor | Micro 桌面包，以及该包要求的运行时；旧公开版另有驱动要求 |
| 用实体手柄操作 Codex | Agent Controller | Controller 桌面包、XInput 兼容手柄；旧公开版完整 Micro 功能另有驱动要求 |
| 在 Codex 对话里调用 Micro 工具 | Micro 插件 | 可选扩展，单独提供安装路线；不是桌面版首次使用的前置条件 |

`codex-control` 是随产品交付的内部组件。运行桌面应用本身不需要获取它的源码或 NuGet 包，也不需要 Git、Visual Studio 或 .NET SDK。两个桌面产品可以分别使用。旧版未签名驱动的本机签名另有 Windows SDK、WDK 等工具要求，见对应驱动指南；这也是旧路线不适合作为新免驱产品默认入口的原因。

截至核对日期，公开入口与开发状态如下：

| 对象 | 状态 | 对安装路线的影响 |
| --- | --- | --- |
| [Agent Controller 1.2.1](https://github.com/gantrol/AgentController/releases/tag/v1.2.1) | 已公开；有自包含包与 `-compact` 包 | 默认选自包含包；不能套用开发分支的完整免驱说明 |
| [Micro Monitor 0.3.10](https://github.com/gantrol/AgentController/releases/tag/codex-micro-monitor-v0.3.10) | 已公开；旧 HID 路线 | 需要 .NET 10 Desktop Runtime x64，完整功能还需要单独安装虚拟 HID 驱动 |
| [独立 Micro 软件接口版](https://github.com/gantrol/codex-micro-monitor) | 新公开仓库已建立、目前为空；开发版本为 `0.3.15-local.1`，源码与新版安装包尚未上传 | 不需要虚拟 HID 驱动或 AgentController；现有桌面 ZIP 打包方式仍要求 Desktop Runtime |
| Controller 软件接口版 | 当前源码已移除主程序的 Broker / VHF 运行依赖；项目版本仍为 `1.2.1` | 源码版本号相同不代表公开 ZIP 已包含改动；需要后续正式发行与验收 |
| Micro 商店渠道 | 已有 MSIX 打包准备，未核实可用商店页面 | 打包方案包含 .NET；上架可用后才能提供商店安装入口 |
| macOS / iOS | 当前没有本路线可交付的客户端 | 不展示为可下载安装的平台 |

## 共同起点

1. 确认使用 Windows x64；项目当前目标为 Windows 10 build 19041+ / Windows 11。
2. 安装并登录 Codex 桌面版，先确认能直接打开一个会话。已有可用 Codex 的用户跳过此步；本产品不能代替 Codex 的安装、登录或账号权限。
3. 按产品进入指定版本的 Release，选择 Windows x64 应用附件。GitHub 自动生成的 `Source code` 压缩包不是可运行应用。
4. 将 ZIP 完整解压到准备长期保留的目录，再运行其中的 EXE；不要直接在压缩包内启动。

若选择需要运行时的包，安装[微软 .NET 10 Desktop Runtime 的 Windows x64 版本](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)。下载页上应选 **.NET Desktop Runtime**；仅安装普通 .NET Runtime 或 ASP.NET Core Runtime 不满足 WPF 桌面程序的要求。

## 现在可下载的 Controller 1.2.1

1. 打开 [1.2.1 Release](https://github.com/gantrol/AgentController/releases/tag/v1.2.1)，默认下载 `AgentController-1.2.1-win-x64.zip`。该包自带运行时。仅在已有 Desktop Runtime、明确希望使用小包时选择 `AgentController-1.2.1-win-x64-compact.zip`。
2. 参照对应 `.sha256` 校验附件，完整解压其中一种包；不要把两种包混在同一目录。
3. 以普通用户运行 `AgentController.exe`，将手柄切换到 XInput 模式并连接 Windows。
4. 启动 Codex，确认 Controller 的目标为 Codex、“桥接”开启，设备页显示已连接的手柄与实时输入。
5. 按 Menu 将 Codex 置于前台，让摇杆回中；选择一个空闲会话，用左摇杆定位并按 A 打开，核对打开的是所选会话。

这条最短路线只能说明程序、手柄和一次基础操作是否可用。公开版未安装驱动时只有有限回退能力，不能据此承诺完整 Micro 功能。需要完整旧 HID 路线时，继续按[该路线安装说明](../CodexMicroSimulator-安装教程.zh-CN.md)处理独立设备支持；不要把此要求带入后续免驱包。

公开程序未签名。若出现 Windows 拦截，先核对下载来源与校验值；首次安装文档应明确这一情况，不能写成必然无提示启动。若首次设置更新了 Codex 快捷键而未生效，正常退出并重开 Codex 后再检查。

## 现在可下载的 Micro Monitor 0.3.10

1. 安装 .NET 10 Desktop Runtime x64。
2. 打开 [0.3.10 Release](https://github.com/gantrol/AgentController/releases/tag/codex-micro-monitor-v0.3.10)，下载 `Codex-Micro-Monitor-v0.3.10-win-x64.zip` 及对应 `.sha256`，校验并完整解压。
3. 完整功能还需该 Release 链接的独立驱动包。关闭 Codex 和 Agent Controller，按[该版本引用的中文驱动指南](https://github.com/gantrol/AgentController/releases/download/codex-micro-v1.2.0/UNSIGNED-DRIVER.zh-CN.md)操作；安装状态达到 `Ready` 后继续，返回 `3010` 时先重启 Windows。已有 `Ready` 安装不必重装。
4. 驱动属于未签名开发者流程，保持 Windows 驱动签名强制检查开启。无法按指南完成时，这条完整功能路线尚未完成。
5. 打开已登录的 Codex，再以普通用户运行 `CodexMicro.exe`，检查额度、任务列表及一次会话切换。

这个公开 ZIP 不包含驱动，也不会自动安装驱动。“下载、解压、启动即可完整使用免驱 Micro”目前不能指向它。

## 新免驱版发布后的目标路线

以下步骤是待交付路线，发布入口与全新安装验收补齐后才可作为普通用户教程。

### Micro 桌面版

1. 从独立产品的正式发布入口下载桌面包。现有 ZIP 命名格式为 `codex-micro-monitor-<版本>-win-x64.zip`；带 `plugin` 的另一个 ZIP 属于插件。
2. 按所选包处理运行时：现有 ZIP 需要 .NET 10 Desktop Runtime x64；商店打包方案自带运行时，须待实际上架后再提供相应入口。
3. 完整解压 ZIP，打开已登录的 Codex，运行 `CodexMicro.exe`。启动代码默认建立软件控制连接。
4. 等待读取到额度与任务状态，选择一个已有的空闲会话并核对 Codex 打开的会话。
5. 在这个会话空闲时切换一次可用模型或推理强度，核对 Codex 显示的选择；不要把已运行中的生成当作切换是否成功的判断依据。

前三步不需要安装 AgentController、虚拟 HID 驱动、Micro 插件或公共组件包。首次操作完成后，再按需要设置置顶、开机启动、尺寸与键帽。

### Controller 桌面版

1. 下载正式发布的免驱 Controller 自包含包，完整解压并运行 `AgentController.exe`。
2. 连接 XInput 手柄，打开已登录的 Codex，确认所选目标与手柄连接状态。
3. 将 Codex 置于前台、手柄回中，完成一次选择会话并打开的操作。

软件接口已接入当前主程序，但真实手柄端到端验收尚待完成。后续版本应以自己的发行说明描述可用动作，不能复用公开 1.2.1 的测试数量作为新版本的验收结果。

## 第一次使用怎样算完成

| 检查点 | 用户看到的结果 | 没通过时先查什么 |
| --- | --- | --- |
| 程序启动 | 对应产品窗口出现，版本与下载页一致 | 是否完整解压、是否下载源码包、是否缺 Desktop Runtime、是否被系统拦截 |
| 目标可用 | Codex 已登录，能直接打开会话 | Codex 本身是否可用；应用连接状态与会话是否存在 |
| Micro 状态读取 | 显示账号可用的额度窗口与实际任务状态 | 读取失败或尚未更新；没有任务与连接失败不能混为一谈 |
| Micro 会话操作 | 点击任务后，Codex 打开同一个会话 | 会话身份、Codex 版本和接口兼容性；连接成功不等于动作成功 |
| Micro 模型操作 | 空闲会话中显示所选模型或推理强度 | 账号是否提供该选项、当前会话是否支持、结果是否已回读 |
| Controller 输入 | 识别手柄，一次选择和打开会话与输入对应 | XInput 模式、当前 Agent、桥接开关、前台窗口、摇杆是否回中 |

首次检查选择一个空闲会话即可。语音、审批、停止运行、DeepSeek 和插件各自作为后续功能，不放进最短安装路径。

反馈时记录产品版本、下载入口、Windows / Codex 版本、失败步骤与错误原文。不要把“窗口打开了”或“连接灯亮了”当作全部功能验收通过。

## 后续启动、更新与移除

- ZIP 路线从已解压的目录启动，可为 EXE 创建快捷方式；开机启动是首次成功后的可选项。
- 更新前从托盘正常退出旧实例，将新包解压到独立目录，核对版本再使用。Micro 具有单实例行为，旧实例未退出时，启动新 EXE 可能只显示旧窗口。
- 若曾开启开机启动，更新后确认它指向新位置；移除前先关闭该选项并退出程序，再删除应用目录。
- ZIP 程序的设置不随应用目录删除：Controller 使用 `%LOCALAPPDATA%\AgentController`，当前 Micro 使用 `%LOCALAPPDATA%\CodexMicro`。保留设置有利于重新安装；完整清理设置应由用户另行选择。
- 上述移除步骤不卸载旧 HID 驱动。旧路线的驱动管理继续按对应驱动指南处理。

## 应先补齐的交付缺口

| 优先级 | 缺口 | 完成条件 |
| --- | --- | --- |
| P0 | 公开下载与免驱说明指向不同实现 | 给两个产品各自明确的下载入口、版本、附件名与依赖；新免驱版有可获取的正式包 |
| P0 | 用户只能从开发说明推导 Micro 安装步骤 | 正式发行附带普通用户安装说明，直接给出 Runtime、EXE 和第一个操作；构建命令移到开发者入口 |
| P0 | 当前源码与旧公开 Controller 共用 `1.2.1` 标识 | 下次正式发布分配可区分的版本并同步 manifest、附件和发行说明；不覆盖旧发行包 |
| P0 | 缺少干净环境的首次使用验收 | 在没有开发环境、旧配置和驱动的环境中，分别完成新包启动与上述关键操作；记录实际支持的 Codex 版本 |
| P1 | Micro ZIP 仍有额外运行时步骤 | 提供已验收的自包含发行方式，或上线包含运行时的商店包；保留现有 ZIP 时明确依赖 |
| P1 | 插件入口尚未验证 | 单独验证市场发现、安装、启用和一次工具调用，确认后再提供插件教程 |
| P1 | 更新和开机启动入口容易指向旧目录 | 验收退出旧实例、更新、保留设置、重启以及启动项指向新版本 |

全新系统、商店、插件和真实手柄验收需要另外执行；本文的梳理没有执行这些操作。现有 README、旧驱动教程与新源码存在版本交叉引用，后续更新应以对应发行包为边界。

## 维护依据

- [产品入口与传播方向](launch-and-readme-outline.zh-CN.md)
- [Controller 免驱装配与验收边界](../architecture/agent-controller-driverless-uml.zh-CN.md)
- [三个仓库的职责与依赖](../architecture/micro-component-dependency.zh-CN.md)
- [Controller 打包脚本](../../scripts/package-release.ps1)与[应用项目](../../app/AgentController.csproj)
- [Controller 默认装配](../../app/Composition/AppComposition.cs)与[默认设置](../../app/Models/AppSettings.cs)

独立 Micro 的版本、桌面与插件附件分别以其 `Version.props`、`scripts/package.ps1` 和插件 manifest 为准；启动方式以 `src/CodexMicro.Desktop/App.xaml.cs` 为准。公开入口出现变化时，应重新核对本页状态表，不能仅依据本机存在打包产物就认定已发布。
