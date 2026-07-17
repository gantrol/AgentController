# Reference

Codex App Server、ChatGPT/Codex Micro 桌面实现、Work Louder 设备协议，以及 Windows 控制器协议。

稳定性最高的是前后两层；中间两层存在版本耦合。

### 1. OpenAI 官方材料

- [Codex App Server 协议说明](https://learn.chatgpt.com/docs/app-server.md)：正式的 Codex 集成接口，采用省略 `jsonrpc` 字段的 JSON-RPC 2.0 风格协议。
- [App Server 官方开源实现](https://github.com/openai/codex/tree/main/codex-rs/app-server)：协议实现、消息类型和生成 Schema 的来源。
- [Codex Micro 使用说明](https://learn.chatgpt.com/docs/features/codex-micro.md)：设备行为、灯光和 Codex 交互说明。
- [桌面命令与快捷键](https://learn.chatgpt.com/docs/reference/commands.md)
- [Follow-up 的 Steer / Queue 设置](https://learn.chatgpt.com/docs/reference/settings#general)
- [Codex CLI 交互快捷键](https://learn.chatgpt.com/docs/developer-commands?surface=cli#cli-interactive-shortcuts)
- [Codex 完整官方手册](https://developers.openai.com/codex/codex-manual.md)
- [Work Louder × OpenAI 产品页](https://openai.com/supply/co-lab/work-louder/)

其中 `Steer` 已有正式协议：

```text
method: turn/steer

params:
  threadId: string
  expectedTurnId: string
  input: UserInput[]
  clientUserMessageId?: string | null

response:
  turnId: string
```

本机 Codex 0.144.1 生成的精确类型：

- [TurnSteerParams.ts](codex-app-server-schema-0.144.1/typescript/v2/TurnSteerParams.ts)
- [TurnSteerResponse.ts](codex-app-server-schema-0.144.1/typescript/v2/TurnSteerResponse.ts)
- [完整 V2 JSON Schema](codex-app-server-schema-0.144.1/json-schema/codex_app_server_protocol.v2.schemas.json)

`expectedTurnId` 是必填的，必须指向当前正在运行的 turn。

### 2. Work Louder 官方材料

- [Creator Micro 2 设置与 Input 配置](https://worklouder.cc/micro-setup)
- [Work Louder Input 发布包](https://github.com/worklouder/input-releases)
- [Creator Micro 2 固件发布](https://github.com/worklouder/cm-v2-fw-releases)
- [Work Louder GitHub 组织](https://github.com/worklouder)
- [Work Louder GitHub Packages](https://github.com/orgs/worklouder/packages)

`@worklouder/device-kit-oai` 不是公开源码协议：本机包标记为 `UNLICENSED`，发布源是私有 GitHub Packages。因此它适合作为当前版本的适配依据，不宜被当成长期稳定的固件 ABI。

### 3. 本机 ChatGPT/Codex 包体

当前安装：

```text
OpenAI.Codex 26.707.12708.0
C:\Program Files\WindowsApps\OpenAI.Codex_26.707.12708.0_x64__2p2nqsd0c76g0
```

主包：

- [app.asar](<C:/Program Files/WindowsApps/OpenAI.Codex_26.707.12708.0_x64__2p2nqsd0c76g0/app/resources/app.asar>)

包内关键位置：

```text
node_modules/@worklouder/device-kit-oai/
node_modules/@worklouder/wl-device-kit/
webview/assets/codex-micro-layout-Dxjuzn6Z.js
webview/assets/codex-micro-bridge-D90_rd6W.js
webview/assets/codex-micro-settings-DzSPVLRQ.js
webview/assets/codex-micro-signals-DPWNMrvO.js
.vite/build/codex-micro-service-CR6sUcZG.js
```

已提取的便于阅读版本：

- [device-kit-oai README](codex-micro-inspect/oai/README.md)
- [RPC 类型声明](codex-micro-inspect/oai/rpc_api_oai.d.ts)
- [RPC 实现](codex-micro-inspect/oai/rpc_api_oai.js)
- [Codex Micro 动作目录](codex-micro-inspect/renderer/codex-micro-layout-Dxjuzn6Z.js)
- [输入与命令分发](codex-micro-inspect/renderer/codex-micro-bridge-D90_rd6W.js)
- [Codex Micro 设置界面](codex-micro-inspect/renderer/codex-micro-settings-DzSPVLRQ.js)
- [设备连接服务](codex-micro-inspect/service/codex-micro-service-CR6sUcZG.js)

重要区别：`settings.codexMicro.*` 只是界面本地化 ID，不是协议方法或动作 ID。实际桌面动作名称在 `codex-micro-layout-*.js` 中。

### 4. 控制器与 USB 官方标准

- [Microsoft XInput 概览](https://learn.microsoft.com/en-us/windows/win32/xinput/xinput-game-controller-apis-portal)
- [`XINPUT_GAMEPAD` 数据结构](https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_gamepad)
- [Microsoft HID 文档](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/)
- [Microsoft GameInput](https://learn.microsoft.com/en-us/gaming/gdk/docs/features/common/input/overviews/input-overview)
- [USB-IF HID 1.11](https://www.usb.org/document-library/device-class-definition-hid-111)
- [JSON-RPC 2.0 规范](https://www.jsonrpc.org/specification)

`XINPUT_GAMEPAD` 明确把十字键作为数字按钮位，把左摇杆作为两个模拟轴，所以“上下左右键”和“左摇杆方向”在协议层可以直接区分。四个背键则不属于标准 XInput 字段，需要 HID、GameInput 扩展或厂商协议。

### 5. 当前项目对应实现

- [v0.4a 语义与频率规范](../docs/interaction-spec-v0.4-controller-mapping.md)
- [v0.4b 实体手柄映射](../docs/interaction-spec-v0.4b-physical-controller-mapping.md)
- [LogicalInput.cs](../app/Controllers/LogicalInput.cs)
- [BuiltInControllerProfiles.cs](../app/Controllers/BuiltInControllerProfiles.cs)
- [XInputNative.cs](../app/Native/XInputNative.cs)
- [XInputService.cs](../app/Services/XInputService.cs)
- [CodexKeybindingService.cs](../app/Services/CodexKeybindingService.cs)
- [CodexComposerService.cs](../app/Services/CodexComposerService.cs)

因此规范中的协议优先级应是：`Codex App Server / 原生模型动作 → ChatGPT 桌面命令 → 快捷键兜底`；Work Louder RPC 单独封装并按包版本检测。
