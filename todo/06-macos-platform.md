# 06 — macOS 平台

> Status: In Progress — Foundation Preview
> Priority: P1
> Depends on: 01-core-architecture, 02-codex-app-server, 05-desktop-ui-ux-and-avalonia

## Foundation Preview 范围

Mac 可以先交付不含虚拟 Micro 的 Foundation Preview，用于验证控制器、任务列表、App Server、语音、Submit、Steer/Queue/Stop、状态反馈和权限引导。它不算与 Windows Full Micro mode 等价的正式完整体验，也不能把 UIA/Accessibility 路径继续扩建成长期替代品。

## 待办

### 控制器输入

- [x] 使用 Apple Game Controller 建立标准设备 backend；当前采用无 held callback 的只读轮询实现，待 Mac 真机矩阵验收。
- [ ] 验证后台事件、连接/断开、多个控制器和 current controller 变化。
  - [x] 建立会话内稳定设备身份与轮询拓扑跟踪器；连接、断开、数组重排和 current controller 切换均有确定事件、修订号与自动化测试。
  - [x] 对 `shouldMonitorBackgroundEvents` 执行启用后的 readback，未确认时不再宣称支持后台输入。
  - [ ] 在 Mac 真机验证通知/轮询一致性、后台、睡眠/唤醒与多设备矩阵后再关闭本项。
- [ ] 读取标准 Profile、用户系统重映射、battery、haptics 和 light 能力。
  - [x] 标准 extended Profile、battery、haptics 与 light 已进入只读快照和 UI；系统重映射仍待真机验证。
- [ ] 为非标准背键评估 IOHID raw backend，并与标准 backend 去重。
- [ ] 建立 Xbox、DualSense、8BitDo 和 Generic 设备矩阵。

### Codex 与系统集成

- [ ] App Server 作为 Thread/Turn 权威通道。
- [ ] Companion 模式单独实现 Accessibility、窗口发现和前台控制。
- [x] 区分 Accessibility、Input Monitoring、Microphone 等权限及其实际用途；预览版不会提前请求后两者。
- [x] 提供只读权限健康页与系统隐私设置入口；实际授权/撤销流程仍待 Mac 真机验收。
- [ ] 实现 Menu Bar、开机启动、单实例和睡眠/唤醒恢复。

### 语音与反馈

- [ ] 验证按住说话、双击锁定、取消和系统麦克风权限。
- [ ] 设备断开、应用退出或权限撤销时可靠结束录音。
- [ ] 验证控制器 haptic 在 macOS backend 的能力差异。

### 打包发行

- [x] 输出 Apple Silicon 与 Intel 双架构 `.app` 策略，并在 Windows 交叉构建中验证各自 Mach-O apphost 与 Avalonia 原生库。
- [ ] 完成 Developer ID 签名、公证、DMG/PKG 和自动更新验证。
- [ ] 在干净用户账户验证 Gatekeeper、权限提示和卸载。

### 原生 Micro 完整模式

- [ ] 以 [ADR-0002](../docs/adr/0002-codex-micro-native-compatibility.zh-CN.md) 的协议与设备身份 Gate 为共同前置条件，Mac 不建立第二份 Micro codec。
- [ ] 首选评估 CoreHID `HIDVirtualDevice` 的最低系统版本、双向 output、vendor-defined descriptor 和 `com.apple.developer.hid.virtual.device` entitlement。
- [ ] CoreHID 不满足时再评估 HIDDriverKit/System Extension；记录 entitlement 申请、用户批准、签名、公证、更新和卸载成本。
- [ ] 分别验证 Input Monitoring、Accessibility、Microphone 与 virtual HID entitlement 的真实用途，不合并或提前请求权限。
- [ ] 不假设 Windows VHF descriptor/安装状态可以直接复用；只共享纯协议、布局和 ActionResult 合同。
- [ ] entitlement、设备身份、Codex detection、睡眠/唤醒和签名公证均通过后，Mac 才进入 Full parity/GA。
- [ ] 若 Apple entitlement 或软件身份路线不可发行，评估与 Windows 共用的预刷写 USB/BLE bridge 作为完整免驱 SKU。

## 完成门槛

- Mac Foundation Preview 不安装虚拟 HID 也能完成语义核心工作流，但下载页和应用内明确标为 Limited/Preview。
- 所有权限均按需请求，并能解释为什么需要。
- 睡眠、断连、Codex 重启和权限撤销不会留下按键、录音或危险状态。
- 已签名公证包可在干净 Mac 上安装、升级和卸载。
- Mac Full parity 必须有 CoreHID 或物理桥的原生 Micro 全链路证据，不能只靠 Accessibility/UIA 类路径宣布完成。
