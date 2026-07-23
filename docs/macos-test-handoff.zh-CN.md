# macOS 真机测试跨电脑接力说明

> 给另一台 Mac 上接手本任务的 Codex 与测试者。无需依赖原电脑的聊天记录；先完整阅读本文件，再执行测试。

## 任务边界

- 远程分支：`codex/macos-foundation-handoff`
- 基线：`main` 的 v1.1 提交 `0385949`
- 目标：验证 macOS Foundation Preview 的 Apple Game Controller 生命周期切片。
- 不在本轮范围：Windows Virtual Micro、Codex App Server 语义动作、语音、Developer ID 正式发行。
- 不要把 Accessibility 当作 Micro 的替代实现，也不要为了启动本地开发构建而关闭 Gatekeeper。

该分支相对 `main` 只应包含 macOS 平台代码、Foundation Preview UI、macOS 测试/打包脚本及相关文档。若看到 `virtual-micro/driver`、`docs/ux` 或 Windows Micro 文件出现在分支差异中，应先停止并检查是否切错分支。

## 已完成的代码

1. `GCController.controllers` 的数组下标不再作为设备身份；native controller 在一次进程会话内获得稳定 ID。
2. 轮询快照会生成确定的 `Connected`、`Disconnected`、`BecameCurrent`、`StoppedBeingCurrent` 事件和拓扑修订号。
3. 只有连接/current 变化才增加修订号；普通按钮和摇杆输入不会增加拓扑修订号。
4. `shouldMonitorBackgroundEvents` 设置为 `true` 后必须 readback 成功，UI 才会显示后台监控可用。
5. Foundation Preview 控制器面板会保留最近一次拓扑事件，便于截图取证。
6. `scripts/publish-macos.ps1` 使用跨平台路径，可在 PowerShell 7 for macOS 上执行。

原电脑上的自动化基线：

- `AgentController.Platform.MacOS.Tests`：16/16 通过；
- `AgentController.sln` Release：909/909 通过；
- Avalonia Desktop Release：0 warning / 0 error；
- Windows 交叉生成 `osx-arm64` 与 `osx-x64` 两个 `.app` 成功。

这些结果不能替代下述 Mac 真机验收。

## 在另一台电脑取得分支

新克隆：

```bash
git clone https://github.com/gantrol/AgentController.git
cd AgentController
git fetch origin
git switch --track origin/codex/macos-foundation-handoff
```

已有仓库：

```bash
git fetch origin
git switch codex/macos-foundation-handoff || \
  git switch --track origin/codex/macos-foundation-handoff
git pull --ff-only
```

必须先确认：

```bash
git status --short --branch
git diff --name-status origin/main...HEAD
git rev-parse HEAD
```

预期工作区干净，分支名正确，差异集中在 `src/AgentController.Platform.MacOS`、`src/AgentController.Desktop`、`tests/AgentController.Platform.MacOS.Tests`、`packaging/macos`、`scripts/publish-macos.ps1`、`docs/macos-*` 与 `todo/06-macos-platform.md`。

## 环境要求

- macOS 14 Sonoma 或更高版本；验收矩阵最终需覆盖 macOS 14、15、26。
- Apple Silicon 使用 `osx-arm64`；Intel 使用 `osx-x64`。
- Git 与 `.NET SDK 10.0.302`；仓库 `global.json` 允许同系列更新补丁。
- 打包脚本需要 PowerShell 7（`pwsh`），直接源码运行不需要 PowerShell。
- 至少一只 Apple Game Controller 支持的手柄；完整矩阵为 Xbox、DualSense、8BitDo、Generic。
- 双手柄用例需要同时连接两只手柄，优先同时覆盖 USB 与 Bluetooth。

记录环境：

```bash
sw_vers
uname -m
dotnet --version
dotnet --info
```

若 `dotnet --version` 不满足 `global.json`，先安装正确 SDK，不要修改 SDK pin 来绕过。

## 构建与自动化基线

```bash
dotnet restore ./AgentController.sln
dotnet test ./tests/AgentController.Platform.MacOS.Tests/AgentController.Platform.MacOS.Tests.csproj \
  --configuration Release --no-restore
dotnet test ./AgentController.sln --configuration Release --no-restore
```

先用源码运行，避免签名和 bundle 干扰控制器验证：

```bash
dotnet run --project ./src/AgentController.Desktop/AgentController.Desktop.csproj \
  --configuration Release
```

再构建本机架构 `.app`：

```bash
# Apple Silicon
pwsh -NoProfile -File ./scripts/publish-macos.ps1 -Runtime osx-arm64

# Intel Mac
pwsh -NoProfile -File ./scripts/publish-macos.ps1 -Runtime osx-x64
```

启动 bundle：

```bash
chmod +x 'artifacts/macos/osx-arm64/Agent Controller.app/Contents/MacOS/AgentController.Desktop'
open 'artifacts/macos/osx-arm64/Agent Controller.app'
```

Intel Mac 将路径中的 `osx-arm64` 改为 `osx-x64`。本地自构建验证不等于 Developer ID 签名、公证或 Gatekeeper 干净账户验收。

## 控制器生命周期测试顺序

每个用例都记录 macOS 版本、CPU 架构、手柄型号/固件、USB 或 Bluetooth、应用前后台状态，以及 UI 中完整的 `Topology rN` 文本。

### A. 单手柄

1. 无手柄冷启动：应显示 `No controller detected`，不出现 Microphone 或 Input Monitoring 请求。
2. 连接手柄 A：只出现一次 `connected`；Apple 将其设为 current 时出现 `became current`。
3. 连续操作按钮、扳机和摇杆 30 秒：输入值应刷新，但 `Topology rN` 不应因普通输入持续增长。
4. 断开 A：出现一次 `stopped being current`（若此前为 current）和一次 `disconnected`；列表清空且无残留按键值。
5. 重新连接 A：获得新的进程会话设备 ID 是允许的；同一次连接期间 ID 不能随轮询变化。

### B. 双手柄与 current 切换

1. 连接 A，再连接 B：列表同时显示两个不同 ID，不能因列表排序变化互换身份。
2. 依次操作 A、B：current 标记应随 Apple Game Controller 的 current 状态变化，任意时刻最多一个 `CURRENT`。
3. current 从 A 切到 B：同一修订中应先看到 A `stopped being current`，再看到 B `became current`。
4. 断开非 current 手柄：另一只手柄及其 current 状态不应被误清除。
5. 断开 current 手柄：不得保留旧 `CURRENT` 或旧输入；系统若选择另一只手柄，应在后续修订中明确显示。
6. 交换 USB/Bluetooth 连接顺序后重复以上步骤。

### C. 后台与睡眠/唤醒

1. Capability 中 `Background controller events` 只有 readback 成功时才显示 `Limited`；否则记录 `Unavailable` 的完整说明。
2. 将应用置于后台至少 30 秒，操作手柄并返回：输入应继续更新或明确显示平台不支持，不能伪报成功。
3. 手柄按下、摇杆偏转时让 Mac 睡眠，然后先释放手柄再唤醒：应用不得留下卡键、非零扳机或陈旧 current 状态。
4. 睡眠期间断开手柄，唤醒后列表必须收敛为空或仅保留真实在线设备。
5. 重复前后台、睡眠/唤醒各 10 次，记录任何重复事件、遗漏断连或 native crash。

### D. Profile 与能力观察

对每个设备记录：

| 字段 | 结果 |
| --- | --- |
| 型号、固件、连接方式 |  |
| `DisplayName` / `ProductCategory` |  |
| 标准按键、摇杆、扳机 |  |
| 系统重映射是否反映到输入 |  |
| Battery 数值是否合理 |  |
| Haptics capability |  |
| Light capability |  |
| 后台输入 |  |
| current 切换 |  |
| 断连/重连 |  |
| 睡眠/唤醒 |  |

能力 flag 只证明 GameController.framework 暴露对象，不证明振动或灯光行为已验证；行为验收仍需后续输出接口。

## 证据与失败记录

建议创建不含个人信息的本地证据目录：

```bash
mkdir -p artifacts/macos-acceptance
{
  git rev-parse HEAD
  sw_vers
  uname -m
  dotnet --info
} > artifacts/macos-acceptance/environment.txt
```

每个失败至少保存：

1. 精确复现步骤和发生时间；
2. 手柄型号、固件、连接方式和其他同时在线设备；
3. 失败前后的控制器 ID、`CURRENT` 标记与 `Topology rN` 文本；
4. UI 截图或录屏；
5. 终端异常、native crash report 或 Console.app 相关片段；
6. 是否只在源码运行、`.app`、前台、后台或睡眠恢复后出现。

不要提交含用户名、蓝牙地址、序列号或完整系统日志的原始文件；先脱敏。

## 通过标准

- 同一次连接期间设备 ID 稳定，双手柄不会因数组换序互换身份。
- 任意快照至多一个 current controller。
- 每次连接、断开和 current 切换只产生一组确定事件；普通输入不推进拓扑修订。
- 后台、断连与睡眠/唤醒后无卡键、残留扳机、陈旧设备或 native crash。
- Foundation Preview 不主动请求 Microphone 或 Input Monitoring。
- Xbox、DualSense、8BitDo、Generic 的矩阵结果均有证据，不用“自动化测试通过”代替真机记录。

## 测试后的代码与文档动作

1. 在 `docs/validation/` 新建带日期的脱敏验收记录，保留环境、矩阵、失败和截图文件名。
2. 根据证据更新 `todo/06-macos-platform.md` 的真机子项；矩阵未完成时不得勾选父项。
3. 修复代码后重新运行 Mac 平台测试与全量 Release 测试。
4. 提交并推送到同一分支 `codex/macos-foundation-handoff`。
5. 不要自行合并 `main`、改写远程历史或删除分支；将结果交还给用户决定。
