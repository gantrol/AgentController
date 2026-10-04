# 平台方向

> 2026-10-03 · 当前决策

## Codex Micro

- 当前 Apple 平台目标是 **macOS 桌面版**，沿用 Windows 的键盘外观与操作语义。
- Windows 与 macOS 在同一个 `codex-micro-monitor` 产品仓库维护，平台实现、构建和安装包分别管理；macOS 工程尚未建立。
- 暂无 iOS 产品设计计划。手机远程或蓝牙连接仅作为未来连接能力考虑，不代表已确定手机客户端或传输实现。
- 最初按 iOS / UIKit 制作过原型；随后用户明确将方向改为 macOS。`virtual-micro/ios` 和独立仓库的 iOS 快照仅为历史产物，不代表当前交付目标。
- macOS 目前只有设计稿，没有可运行客户端；技术栈和桌面适配仍待落实。iOS 的 WSS 客户端及配套 Host 提案不能视为 macOS 已完成的实现。
- 设置按桌面宽窗口、左侧分类、右侧常驻预览继续设计，见[设置与尺寸规则](codex-micro-settings-interaction.zh-CN.md)。
- Micro 的产品实现归独立仓库 `codex-micro-monitor`；共用控制组件归 `codex-control`，两个产品分别引用固定版本包。见[仓库与组件边界](micro-component-dependency.zh-CN.md)。

## AgentController 旧 Avalonia Foundation Preview

此路线已废弃，不再作为 macOS 交付计划或 Micro 的桌面实现基础。

- `AgentController.Desktop`、`AgentController.Platform.MacOS` 及对应平台测试退出默认解决方案。
- 删除 `release:macos` 工作区命令，旧 `publish-macos.ps1` 入口停止执行打包。
- 源码、测试和打包资源保留为历史参考；相关指南和路线图标为废弃。
- Windows WPF 客户端继续维护。Micro 的 macOS 方向独立推进，不受旧预览废弃影响。
