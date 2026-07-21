# macOS Foundation Preview

[English](./macos-foundation-preview.md)

这是按路线图新增的 macOS 可运行基础版本，不是 Windows Full Micro 的等价移植。
它用于在真机上验证共享桌面壳、Apple Game Controller、权限和 Codex CLI 环境，
不会安装 Windows VHF，也不会用 Accessibility 冒充 Micro。

## 当前包含

- Avalonia 12.1 / .NET 10 共享桌面壳，最低 macOS 14；
- 原生 Menu Bar、Dock 菜单和进程级单实例；
- Apple Game Controller 标准扩展 Profile、多手柄/current controller、摇杆、按键、
  扳机、battery、haptics 与 light 能力观察；
- Accessibility、Input Monitoring、Microphone 分项状态与系统隐私设置入口；
- Codex CLI 路径探测；
- Apple Silicon (`osx-arm64`) 与 Intel (`osx-x64`) 两个自包含 `.app` 包。

## 明确不包含

- Codex App Server 的 Thread/Turn 动作客户端；
- 语音录制、Submit、Steer/Queue/Stop 等生产动作；
- CoreHID 虚拟 Micro、HIDDriverKit/System Extension 或相关 entitlement；
- Developer ID 签名、公证、DMG/PKG 和自动更新。

因此当前 UI 只读展示手柄与平台状态，不向 Codex 发送动作。上述能力完成真机、签名和
安全验收前，产品内始终显示 `LIMITED PREVIEW`。

## 构建双架构应用包

在仓库根目录运行：

```powershell
dotnet restore .\AgentController.sln
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-macos.ps1
```

输出：

```text
artifacts/macos/osx-arm64/Agent Controller.app
artifacts/macos/osx-x64/Agent Controller.app
```

脚本会检查 apphost 的 Mach-O CPU 类型、`Info.plist` 和
`libAvaloniaNative.dylib`。Windows 文件系统不能保存 Unix 可执行位；复制到 Mac 后，
在对应架构目录运行：

```bash
chmod +x 'Agent Controller.app/Contents/MacOS/AgentController.Desktop'
open 'Agent Controller.app'
```

不要关闭 Gatekeeper。公开分发前必须在 Mac 上用 Developer ID、hardened runtime
和 `packaging/macos/FoundationPreview.entitlements` 正确逐项签名，再完成公证与
staple。参考 [Avalonia macOS 部署说明](https://docs.avaloniaui.net/docs/deployment/macos)。

## 真机验收清单

- [ ] macOS 14、15、26 各至少启动一次；
- [ ] Apple Silicon 与 Intel 包分别在对应架构运行；
- [ ] Xbox、DualSense、8BitDo、Generic 手柄连接/断开、多手柄和 current 变化；
- [ ] 后台、睡眠/唤醒后无卡键或陈旧状态；
- [ ] Menu Bar、Dock 菜单、单实例和退出生命周期；
- [ ] 三类权限均按用途显示，预览版不弹麦克风或 Input Monitoring 请求；
- [ ] Developer ID 签名、公证及干净账户 Gatekeeper 验证。

当前 Windows 交叉构建只能证明项目、XAML、两种 Mach-O apphost 和 Avalonia 原生库
完整，不能替代以上 Mac 真机验收。
