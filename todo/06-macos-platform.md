# 06 — macOS 平台

> Status: Planned
> Priority: P1
> Depends on: 01-core-architecture, 02-codex-app-server, 05-desktop-ui-ux-and-avalonia

## MVP 范围

Mac 首版不以虚拟 Micro 为前置条件。目标是完成：控制器连接、任务列表、切换、语音、Submit、Steer/Queue/Stop、状态反馈和安全权限引导。

## 待办

### 控制器输入

- [ ] 使用 Apple Game Controller 建立标准设备 backend。
- [ ] 验证后台事件、连接/断开、多个控制器和 current controller 变化。
- [ ] 读取标准 Profile、用户系统重映射、battery、haptics 和 light 能力。
- [ ] 为非标准背键评估 IOHID raw backend，并与标准 backend 去重。
- [ ] 建立 Xbox、DualSense、8BitDo 和 Generic 设备矩阵。

### Codex 与系统集成

- [ ] App Server 作为 Thread/Turn 权威通道。
- [ ] Companion 模式单独实现 Accessibility、窗口发现和前台控制。
- [ ] 区分 Accessibility、Input Monitoring、Microphone 等权限及其实际用途。
- [ ] 提供权限健康页、系统设置入口和重启提示。
- [ ] 实现 Menu Bar、开机启动、单实例和睡眠/唤醒恢复。

### 语音与反馈

- [ ] 验证按住说话、双击锁定、取消和系统麦克风权限。
- [ ] 设备断开、应用退出或权限撤销时可靠结束录音。
- [ ] 验证控制器 haptic 在 macOS backend 的能力差异。

### 打包发行

- [ ] 输出 Apple Silicon 与 Intel 架构策略。
- [ ] 完成 Developer ID 签名、公证、DMG/PKG 和自动更新验证。
- [ ] 在干净用户账户验证 Gatekeeper、权限提示和卸载。

### 虚拟 Micro 研究（MVP 之后）

- [ ] 评估 CoreHID `HIDVirtualDevice` 的最低系统版本和 entitlement。
- [ ] 评估 HIDDriverKit/System Extension 的 entitlement、安装和分发成本。
- [ ] 不假设 Windows VHF descriptor 可以直接复用。
- [ ] entitlement、设备身份和 Codex 检测均验证后再做 Go/No-Go。

## 完成门槛

- Mac MVP 不安装虚拟 HID 也能完成核心工作流。
- 所有权限均按需请求，并能解释为什么需要。
- 睡眠、断连、Codex 重启和权限撤销不会留下按键、录音或危险状态。
- 已签名公证包可在干净 Mac 上安装、升级和卸载。
