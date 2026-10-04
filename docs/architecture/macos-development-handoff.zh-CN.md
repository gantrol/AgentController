# macOS 开发交接入口

更新：2026-10-04。

当前 Apple 平台任务是 **Codex Micro Monitor 的 macOS 客户端**。权威交接文档位于独立产品仓库：

[Codex Micro Monitor：macOS 开发交接](https://github.com/gantrol/codex-micro-monitor/blob/main/docs/architecture/macos-development-handoff.zh-CN.md)

本文仅标明 AgentController 边界，避免在两处维护同一份实施计划。

- AgentController 继续维护 Windows WPF 实体手柄、输入状态机、Overlay 与产品动作路由；不作为 Micro 的项目引用或源码依赖。
- 两个产品共同依赖独立 `codex-control` 固定版本包。现有公共合同可供 macOS 对照，但 Windows 命名管道、可执行文件发现与 UI Automation 不是 macOS 实现。
- `scripts/publish-macos.ps1` 已停止执行；`AgentController.Desktop`、`AgentController.Platform.MacOS` 及相关测试是已废弃的 Avalonia Foundation Preview 历史，不应恢复为本轮交付。
- `virtual-micro/ios` 是历史 UIKit 原型。Micro 新工程与后续 macOS 构建、设置、权限和发行均在独立产品仓库维护。
- 新的手柄版 macOS 产品需要单独需求决定；本次 Micro 源码发布不包含 AgentController 的 macOS 安装包。

详见[平台方向](platform-direction.zh-CN.md)与[仓库边界](micro-component-dependency.zh-CN.md)。
