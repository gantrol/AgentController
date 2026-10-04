# 三仓库与共用控制组件

| 仓库 | 当前维护范围 |
| --- | --- |
| `D:\AgentTools\AgentController` | 手柄、映射、Overlay、Agent 选择，以及产品动作到共用接口的转换 |
| `D:\AgentTools\codex-micro-monitor` | 独立 Micro 桌面、键帽、设置、灯效、监控与插件；macOS 产品方向 |
| `D:\AgentTools\codex-control` | 共用 Codex 客户端、协议连接、模型目录、Windows 界面适配和状态回读 |

本机统一入口为 `D:\AgentTools\manage.cmd`，例如 `build all Debug`、`build all Release`、`run controller Debug` 和 `run micro Release`。三个仓库仍各自管理 Git；旧路径由目录联接兼容。工作区操作见父目录 `README.md`。

两个产品各自引用 `CodexControl` / `CodexControl.Windows`，没有产品之间的源码引用或 Git submodule。组件版本为 `0.1.0-local.2`，在各自 `Directory.Packages.props` 中使用精确版本范围固定。

AgentController 保留 `CodexThreadNavigationExecutor` 和 `CodexUiActionExecutor`，负责自身 Action、结果和门禁映射。Windows 控制实现与三个原 Micro 共享源文件已迁入组件，主程序不再链接 `virtual-micro` 的桌面源码。

先在 `codex-control` 执行 `scripts/package.ps1`，再在任一产品仓库导入：

```powershell
.\scripts\import-control-packages.ps1 -PackageDirectory ..\codex-control\dist\0.1.0-local.2\packages
```

本地包源为 `.artifacts/control-packages`。新检出必须先导入；尚未发布 NuGet/GitHub，不能假定公网存在这些包。组件变更使用新版本，禁止覆盖相同版本的不同内容。

现有命名空间暂时保留以维持源码兼容；生产程序集不再通过 `InternalsVisibleTo` 使用共用组件内部实现。原有测试程序集保留必要的内部访问，本轮未编写或修改测试代码。

`virtual-micro` 中的旧桌面、宿主和插件代码仅供历史查阅，已退出默认解决方案与当前 Micro 发布入口。最新键帽、设置、官方图形目录、Windows 操作和三个未提交的草稿模型修复已按快照迁入独立仓库。原未提交文件保持原位；迁移源哈希记录在 Micro 的 `docs/migration-2026-10-03.json`。

HID 协议与 Broker 作为历史路线及测试留在本仓库；AgentController 默认启动和主程序项目已移除它们的运行依赖，手柄旋钮、语音与动作入口使用软件接口。迁移边界与验证限制见[免驱版 UML](agent-controller-driverless-uml.zh-CN.md)。macOS 尚无客户端或本机 Host；未来 Swift 实现以共享命令/状态合同接入，不直接加载 NuGet，也不恢复已废弃的 Avalonia 预览。
