# ADR-0001：以独立提交迁移到 .NET 10 LTS

> Status: Implemented for CLI; Visual Studio 2026/MSBuild 18 required
> Date: 2026-07-17

## 背景

当前 `app` 与 `app.Tests` 使用 `net9.0-windows10.0.19041.0`，本机只安装了 .NET SDK 9.0.301。微软支持策略在本 ADR 日期将 .NET 10 列为 Active LTS、支持至 2028-11-14；.NET 9 为 STS Maintenance、支持至 2026-11-10。

来源：

- [.NET 官方支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [SDK-style 项目的 Target Framework](https://learn.microsoft.com/en-us/dotnet/standard/frameworks)

核心架构拆分、行为重构和 TFM/包升级若混在一起，失败时难以区分是业务回归、编译器变化、Windows Desktop runtime 变化还是依赖包变化。

## 决策

- 目标框架基线为 .NET 10 LTS。
- 当前结构基线先继续使用 net9，完成 solution、共同属性和集中包版本管理并运行现有测试。
- 安装可用的 .NET 10 SDK 后，在独立提交中加入 `global.json`、更新 TFM，并升级必须同主版本匹配的 Microsoft 包。
- .NET 10 提交不得同时抽离 `MainWindow`、改变手柄路由、重做 UI、引入 App Server 或变更 Micro 行为。
- 新的跨平台 Domain/Application 项目在升级完成后以 `net10.0` 建立；Windows UI/adapter 使用相应 Windows TFM。
- `global.json` 的精确 SDK feature band 在实施当天根据 CI 与开发环境共同可用版本选择，并允许安全 patch roll-forward；不在规划文档中锁定尚未安装的 SDK。

## 实施步骤

1. 记录 net9 基线：restore、build、全部测试和打包脚本结果。
2. 在 net9 上引入 `AgentController.sln`、`Directory.Build.props`、`Directory.Packages.props`，确认无行为变化。
3. 在开发机和 CI 安装同一 .NET 10 SDK feature band。
4. 单独提交 `global.json`、TFM 和必要包主版本升级；只处理编译、分析器和运行时兼容问题。
5. 重跑单元、集成、README 基础实机验收和干净账户打包。
6. 若出现无法隔离的行为回归，回滚整个升级提交，不在升级提交中顺手重构业务代码。

## 结果

- 获得跨 Windows/macOS 的受支持 LTS 基线，并给 Avalonia 与新 class library 统一目标。
- 迁移需要一次额外的机械提交和双环境验证，但显著降低架构拆分期间的诊断成本。
- 在 .NET 10 SDK 尚未安装且基线测试未记录前，不把当前项目改成 `net10.0`。

## 实施记录

### 2026-07-17：net9 结构基线完成

- 新建 `AgentController.sln`，纳入 `app` 与 `app.Tests`。
- 新建 `Directory.Build.props`，集中 Nullable、ImplicitUsings 和 deterministic build 设置。
- 新建 `Directory.Packages.props`，集中 Microsoft.Data.Sqlite、Microsoft.NET.Test.Sdk 与 xUnit 版本；项目文件不再内联包版本。
- `dotnet restore AgentController.sln`：通过。
- `dotnet build AgentController.sln --no-restore --configuration Release`：通过，0 warning、0 error。
- `dotnet test AgentController.sln --no-build --configuration Release`：通过，587 passed、0 failed、0 skipped。
- 本记录只证明自动化 build/test 基线，不替代 README 真实手柄与真实 Codex 验收。

该步骤已于同日完成，见下一条实施记录。

### 2026-07-17：.NET 10 升级完成，IDE 前置条件明确

- 通过 winget 安装 .NET SDK 10.0.302，以 `global.json` 锁定该 feature band，并将所有项目恢复为 `net10.0`/`net10.0-windows10.0.19041.0`。
- Microsoft.Data.Sqlite 使用 10.0.10；继续显式使用 [SQLitePCLRaw.lib.e_sqlite3 3.53.3](https://www.nuget.org/packages/SQLitePCLRaw.lib.e_sqlite3)，替换命中 [GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q) 的 2.1.11 原生 payload。
- `dotnet build`、609 项自动化测试、NuGet audit 和 win-x64 自包含打包均通过。
- Visual Studio Community 2022 17.14.7 使用 MSBuild 17.14.14，加载 solution 时明确报告 SDK 10.0.302 至少需要 MSBuild 18.0，随后所有 SDK-style 项目报 `MSB4236`。
- 曾短暂锁回 net9，以证明 VS 失败只来自 SDK/MSBuild 兼容性；VS 2022 的 Debug/Release Rebuild 随即通过。该诊断回退已撤销，不作为最终项目基线。
- 项目决定保留 .NET 10，开发者应安装 Visual Studio Community 2026（winget ID `Microsoft.VisualStudio.Community`，当前稳定版 18.8.0）或其他带 MSBuild 18 的兼容工具链。
- 升级 VS 后仍需在 IDE 内复验 Debug/Release；在此之前 CLI 是已验证的构建入口。
