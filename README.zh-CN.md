# Agent Controller

[![README in English](https://img.shields.io/badge/README-English-blue.svg)](README.md)
[![简体中文说明](https://img.shields.io/badge/README-简体中文-red.svg)](README.zh-CN.md)

![version](https://img.shields.io/badge/version-0.7-blue) ![platform](https://img.shields.io/badge/platform-Windows-lightgrey)

---

Codex Micro很快就断货了，Codex 专用的迷你键盘。但你注意到没有：

- Codex小键盘有一个旋钮、一只摇杆，手柄也有俩摇杆。
- Codex小键盘有十二个键，没有背键的手柄也有十四个键。
- Codex小键盘丑且不符合人体工学设计。
- Codex小键盘价格加运费，可以买很多只手柄。
- Codex小键盘要等邮寄。
- Codex小键盘不能用来玩游戏。

手柄完胜，证毕。（当然，摸着良心说，六个变色灯，还是挺少手柄能那么瞎眼的。）

于是，研究对接方案，又注意到（注意力不够就用AI注意）：

- Codex小键盘的SDK是随便接入的
- Codex可以编程、建模
- 很多手柄也可编程、建模

那么，就可以有那么一个软件，让手柄去代替Codex小键盘。

我指挥Codex，一下午就做出来了，咱们试试。

可惜交互有一定问题，不过呢，经过一晚上。

（当前以 8BitDo Ultimate 2 、Xbox Series、Flydig Vader 4 Pro 实测）

> ⚠️ **安全提示——使用前请读**
>
> 这个实验性 v0.7 原型由 **Codex 使用 GPT-5.6 Sol 在一天内完成**，没有经过独立的人工代码或安全审计。Codex 更新后，UI Automation 与快捷键可能失效或误操作；程序也未签名。请先审查源码、只用非关键任务试用，并自行承担全部风险。应用在你机器上会做的事：
>
> - 向 **Codex 窗口**发送键盘快捷键与 UI Automation 指令（默认仅在 Codex 位于前台时生效；连接的手柄回中后会自动启用）；
> - **只读**读取 Codex 本机任务数据（`~/.codex`）；
> - 在 `%LOCALAPPDATA%` 写入自身设置，并可向 Codex 快捷键配置追加降级绑定（F17/F18/F20/F22）；
> - 可选注册开机自启（默认关闭）；
> - **不发起任何网络请求**（唯一涉网行为是在浏览器中打开厂商/Codex 链接）。

Agent Controller 是独立实验项目，与 OpenAI、Codex、Work Louder 没有隶属、授权或背书关系。

### 使用要求

- Windows 10（build 19041+）或 Windows 11
- 已安装 Codex 桌面版
- XInput 兼容手柄（实测设备为 8BitDo Ultimate 2；其他 XInput 手柄大多可用，Xbox 与飞智在测试计划中）

### 从 Release 下载安装

1. 到 [Releases](../../releases) 下载最新 zip。
2. 解压到任意目录，运行 `AgentController.exe`。
3. 程序未签名，Windows SmartScreen 可能拦截：点 **更多信息 → 仍要运行**（见上方安全提示；介意可自行构建）。
4. 手柄以 XInput 模式连接，启动 Codex。设备页显示 `LIVE` 即就绪。v0.7 Windows 包为自包含版本，不需要另装 .NET Runtime。

### 默认操作

| 输入 | 动作 |
| --- | --- |
| Menu | 需要时唤醒并置前 Codex；Codex 已在前台时，连接的手柄回中后自动启用，无需再按 Menu |
| 左摇杆 ↑↓ | 在本软件的稳定侧边栏滚轮中移动，不自动打开 |
| 左摇杆 ← | 从项目任务返回；Base 层向右不执行动作 |
| L3 | 循环根区域：置顶任务 → 置顶项目 → 项目 → 未归项目 |
| Y | 打开动作面板；面板内按十字键 ↑ 新建任务 |
| 十字键 ↑ / ↓ | 上一条 / 下一条用户消息；按住 ↑ 4 秒置顶，按住 ↓ 3 秒置底 |
| 右摇杆（简易） | ←→ 调整 Codex 实时 Power；↑ 选择 Standard；↓ 选择 Fast |
| 右摇杆（高级） | ←→ 选择 Model / Effort / Speed；↑↓ 按界面顺序调整账户实际提供的档位（低档在最上） |
| R3 单击 / 长按 | 打开对应选择器；再次单击或按 B 关闭 / 打开手柄设置 |
| A | 进入焦点项目或打开焦点任务 |
| LT（按住） | 按住说话（语音输入），松开结束 |
| X | 发送提示词 |
| B | 短按关闭菜单或撤回最近导航；长按 3 秒并经过屏幕倒计时后取消当前会话 |

右摇杆保持同一方向时会在约 2 秒内逐渐加速；第一次动作立即发生，倾斜越深，最终重复速度越快。

当实时选择为 Sol Max、Codex 不提供简易 Power 时，程序会询问是否改为高级模式：A 切换，B 保持简易；无论选择哪一项，Standard / Fast 仍可使用。

### 从源码构建

```powershell
dotnet build app/AgentController.csproj -c Release
dotnet test app.Tests/AgentController.Tests.csproj -c Release
./scripts/package-release.ps1
```

编译产物位于 `app/bin/Release/net9.0-windows10.0.19041.0/`。封包脚本会在 `dist/` 生成自包含 x64 zip 与 SHA-256 校验文件。

### 仓库结构

本仓库后续仅提交：

- `app/` —— Windows（WPF）应用，行为以它为准；
- `app.Tests/` —— 手柄、本地化、导航与适配器行为的回归测试；
- `scripts/` —— 可复现的 Release 封包脚本；
- `public/docs/` —— 当前指令、版本说明和实验计划；
- `todo.md` —— 路线图与备忘。

### 致谢

手柄插图源自 CREATRBOI 的 "White XBOX Controller" 模型；许可证与署名文件随应用分发（Release 内 `THIRD-PARTY/` 目录）。
