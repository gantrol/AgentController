# 源码同步说明：2026-10-04

本轮同步当前 Windows 源码、免驱控制边界和 macOS 开发交接入口。AgentController 保持独立的 `1.2.1` 源码版本；Micro 保持已上架的 `0.3.15.0` / GitHub `v0.3.15`，不代表 AgentController 重新发布同一版本的二进制。

共享控制依赖固定为 `CodexControl` / `CodexControl.Windows` `0.1.0-local.2`。先从 [codex-control Releases](https://github.com/gantrol/codex-control/releases) 取得对应包并执行 `scripts/import-control-packages.ps1 -PackageDirectory <目录>`，再构建 Windows 主项目。不得用不同内容覆盖同名包。

## 验证状态

当前 Windows 主项目可编译。完整 `dotnet test AgentController.sln -c Release` 尚未通过：

- Domain 15 项、Application 14 项、MicroBroker 24 项通过。
- Architecture 22 项通过、2 项失败。失败断言仍要求 WPF 主项目引用旧 MicroBroker，以及右旋钮调用旧 HID 路由；与当前免驱适配结构不一致。
- `app.Tests` 的 `ControllerTutorialViewDesignTests` 仍引用已不存在的教程控件字段，因此测试项目无法编译。

没有改写测试或为了旧断言恢复被移除的实现。本轮不发布 AgentController 新二进制；后续修复测试与新交付验收需要单独安排。未执行真实 UI 手动测试、安装测试或物理手柄验收。

macOS 当前方向见[交接入口](../architecture/macos-development-handoff.zh-CN.md)。旧 Avalonia Foundation Preview 已停止维护，不能当作现成的 Micro macOS 客户端。
