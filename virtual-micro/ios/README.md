# Codex Micro iOS · 0.1.0

> 历史原型（2026-10-03）：用户已将当前目标改为 macOS 桌面版。此目录保留先前 UIKit 实现供参考，不再作为当前交付路线。见[平台方向](../../docs/architecture/platform-direction.zh-CN.md)。以下内容仅描述此 iOS 快照。

UIKit 原型，iOS / iPadOS 17+。首屏采用当前 Windows 版的 590×610 设计坐标、外壳比例、4×4 键位和双页布局。没有 AgentController 工程依赖，也没有 Swift Package 依赖。

## 打开工程

在 Mac 上用 Xcode 15 或更新版本打开 [CodexMicro.xcodeproj](CodexMicro.xcodeproj)，选择 `CodexMicro` scheme 和 iPhone / iPad 模拟器运行。真机需要在 Signing & Capabilities 选择自己的 Team，并按需更改 Bundle Identifier。

也可从仓库根目录执行构建：

```sh
xcodebuild -project virtual-micro/ios/CodexMicro.xcodeproj \
  -scheme CodexMicro -configuration Debug \
  -destination 'generic/platform=iOS Simulator' \
  CODE_SIGNING_ALLOWED=NO build
```

当前开发环境为 Windows：已做 Swift 语法解析、工程引用和资源格式静态检查；尚未使用 Xcode 编译，未运行 UI / 手动测试。此工程用于原型开发，未配置 App Store 图标与提交材料。

## 与 Windows 保持一致的界面

[查看本版矢量图标](Design/command-icons.svg) · [调整前后对照：上旧下新](Design/command-icons-comparison.svg)

控制页：

|  |  |  |  |
| --- | --- | --- | --- |
| 白色旋钮 | 任务 1 | 任务 2 | 黑色摇杆 |
| 任务 3 | 任务 4 | 任务 5 | 任务 6 |
| Fast | 批准 | 拒绝 | 分叉 |
| 额度旋钮 | 双宽语音键 | ← | Codex |

监控页有 14 个任务键，左下额度旋钮与右下 Codex 键保持原位。窄屏按比例缩放，横屏高度不足时可纵向滚动，键位不重排。

- 白色键帽、圆形凹槽、透明外壳、薄荷色选中边缘，均使用 UIKit / Core Animation 绘制。
- 命令图标沿用 Windows 的含义与轮廓，采用 24×24 画布、约 1.8 的主线宽和圆角端点；分叉节点较密，使用 1.7 线宽作视觉补偿。闪电中段加宽，麦克风内外轮廓拉开间距；Codex 标识保留 Windows 矢量原形并调整视觉大小。
- 状态灯沿用 Windows 的蓝 / 绿 / 橙 / 红；底部额度旋钮包含双额度环、七段数字与三灯。
- 顶部只有连接状态和设置入口，底部只有当前任务、模型与强度；没有说明性文案区。

## 已接入的交互

| 输入 | 行为 |
| --- | --- |
| 任务键轻点 / 长按 | 选择目标 / 桌面打开与停止菜单 |
| 页面圆点、左右滑动 | 控制页与监控页切换 |
| 白色旋钮旋转 | 调整所选任务的推理强度；VoiceOver 支持增减 |
| 白色旋钮、额度旋钮轻点 / 长按 | 切换模型 / 打开模型菜单 |
| 摇杆向上 | 切换 Plan |
| Fast / 批准 / 拒绝 / 分叉 | 按当前目标与能力执行；审批显示具体请求摘要 |
| Codex 键 | 请求在电脑打开所选任务 |

语音键保持双宽外形但禁用；摇杆其余方向没有绑定动作。监控页目前最多展示 14 个任务，没有完整任务列表与分页。

## DEMO 与连接

首次及每次重新启动进入明确标记的 `DEMO` 模式。演示数据保存在内存，可体验选择、状态变化、模型、强度、Fast、Plan、审批和分叉。桌面打开在演示中只模拟回执，不调用 Codex。

连接页支持手动输入 WSS 地址、设备令牌及可选的证书 SHA-256 指纹，凭据保存在 Keychain。没有指纹时使用系统 TLS 信任；填写指纹时信任该主机匹配的叶证书，可用于私有局域网证书。应用不跟随 HTTP 重定向。

**本目录未提供配套 Host 服务，尚不能直接连接 Windows 小键盘或 Codex Desktop。** WSS 客户端实现的是 [v0.1 协议子集](PROTOCOL.md)，需要 Host 实现同一合同后联调。Bonjour、扫码配对、配对令牌交换、增量 patch、自动退避重连、语音均未实现；失败后可从连接页再次连接。

进入后台或失去活动状态会断开连接，返回时重新握手、获取租约与快照。未知结果的请求保留在本地，重连只查询原请求；不会自动重放写操作。当前采用全局单在途请求，结果未确认时禁止继续提交。

## 代码入口

- [MicroDeviceView](CodexMicro/UI/MicroDeviceView.swift)：Windows 坐标、双页布局与控件绑定。
- [MicroStore](CodexMicro/Core/MicroStore.swift)：目标、能力、生命周期、操作回执与待确认记录。
- [WebSocketTransport](CodexMicro/Connection/WebSocketTransport.swift)：WSS、协议握手、租约与快照。
- [DemoTransport](CodexMicro/Connection/DemoTransport.swift)：本地演示行为。
- [UIKit 架构与 UML](../../docs/architecture/codex-micro-ios-uikit-uml.zh-CN.md)：已实现类图与后续 Host 设计。
- [设置、键帽编辑与尺寸规则](../../docs/architecture/codex-micro-settings-interaction.zh-CN.md)：后续交互改版提案，尚未实现。
