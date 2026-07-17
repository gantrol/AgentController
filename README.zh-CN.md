# Agent Controller

[![README in English](https://img.shields.io/badge/README-English-blue.svg)](README.md)
[![简体中文说明](https://img.shields.io/badge/README-简体中文-red.svg)](README.zh-CN.md)

![version](https://img.shields.io/badge/version-0.7-blue) ![platform](https://img.shields.io/badge/platform-Windows-lightgrey)

---

Codex Micro很快就断货了，这款专为Codex设计的小键盘，你想买吗？但你注意到没有：

- Codex小键盘有一个旋钮、一只摇杆，手柄也有俩摇杆。
- Codex小键盘有十二个键，没有背键的手柄也有十四个键。
- Codex小键盘其实不算好看，且不符合人体工学设计。
- Codex小键盘价格加运费，可以买很多只手柄。
- Codex小键盘要等邮寄。
- Codex小键盘不能用来玩游戏。

手柄完胜，证毕。（当然，摸着良心说，六个变色灯，还是挺少手柄能那么瞎眼的。）

> 对了，需要额外的麦克风，这俩一般都不能录音。

于是，研究对接方案，又注意到（注意力不够就用AI注意）：

- Codex小键盘的SDK是随便接入的；
- Codex可以编程、建模，也有快捷键；
- 很多手柄也可编程、建模。

那么，就可以有一个软件，让手柄去代替Codex小键盘。

我指挥Codex，两小时就做出样品，磨了一天交互（跟Codex的界面搏斗），终于基本成型。

- 首先是启动Codex桌面应用(ChatGPT)，或者将应用置于前台——菜单键负责（有的手柄这个键叫Start，或者+）；
- 分层遍历目录，左摇杆负责：
  - 上或下，在同级项目中移动；
  - 左，退出项目；右，进入项目；
  - 在任务时，A键进入任务，不用“右”的原因是，Codex有时加载太卡了；
  - 按压左摇杆可以快速跳转置顶任务、置顶项目、项目、任务四个区域；
- 模型选择（含快速模式），右摇杆负责：
  - 随便动一下右摇杆，模型选择框弹出
  - 按B关闭相关窗口。这步响应比较慢；
- 如何录音？一直按着LT键，就是左手上方运动浮动很大的扳机键。松开就是停止。
- 如何发送？X键；
- 如果想删除输入框的全部内容呢？按Y，再按A，继续按A；
- 如果发送后想取消呢？长按B键 3 秒，会有屏幕倒计时；
- 如何回到上一个问题？十字键的上键。显然，下键就是回到下一个问题。特殊地，长按下键可以回到底部。注意：由于目前Codex本身快捷键有缺陷，而目前下键是模拟发送快捷键，两者合一导致有时按下键会无法跳转；
- 如何新建会话？按Y，按上键；

<!--TODO: 这时怎么切换项目？还是说不做？-->

至此，纯Vibe Coding功能，基本完成。

首次公开版本，用 8BitDo Ultimate 2 、Xbox Series、Flydig Vader 4 Pro 实测上述常用功能。某热心网友用几十块的小鸡手柄，连接也没什么问题。

你可能还会问，Codex Micro的六个Agent键呢？按LB键（LT扳机键上面那个），再按相应按钮。

> ⚠️ **安全提示——使用前请读**
>
> 这个实验性 v0.7 原型由 **Codex 使用 GPT-5.6 Sol 在一天内完成**，没有经过独立的人工代码或安全审计。Codex 更新后，UI Automation 与快捷键可能失效或误操作；程序也未签名。请先审查源码、只用非关键任务试用，并自行承担全部风险。应用在你机器上会做的事：
>
> - 向 **Codex 窗口**发送键盘快捷键与 UI Automation 指令（默认仅在 Codex 位于前台时生效；连接的手柄回中后会自动启用）；
> - **只读**读取 Codex 本机任务数据（`~/.codex`）；
> - 在 `%LOCALAPPDATA%` 写入自身设置，并可向 Codex 快捷键配置追加降级绑定（F17/F18/F20/F22）；
> - 可选注册开机自启（默认关闭）；
> - **不发起任何网络请求**（唯一涉网行为是在浏览器中打开厂商/Codex 链接）。
>
> Agent Controller 是独立实验项目，与 OpenAI、Codex、Work Louder 没有隶属、授权或背书关系。

### 使用要求

- Windows 10（build 19041+）或 Windows 11
- 已安装 Codex 桌面版
- XInput 兼容手柄（实测设备为 8BitDo Ultimate 2；其他 XInput 手柄大多可用，Xbox 与飞智在测试计划中）
- 语音需要自备麦克风，或者手柄自带

### 从 Release 下载安装

1. 到 [Releases](../../releases) 下载最新 zip。
2. 解压到任意目录，运行 `AgentController.exe`。
3. 程序未签名，Windows SmartScreen 可能拦截：点 **更多信息 → 仍要运行**（见上方安全提示；介意可自行构建）。
4. 手柄以 XInput 模式连接，启动 Codex。设备页显示 `LIVE` 即就绪。v0.7 Windows 包为自包含版本，不需要另装 .NET Runtime。

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

本仓库目前仅提交：

- `app/` —— Windows（WPF）应用，行为以它为准；
- `app.Tests/` —— 手柄、本地化、导航与适配器行为的回归测试；
- `scripts/` —— 可复现的 Release 封包脚本；
- `public/docs/` —— 当前指令、版本说明和实验计划；
- `todo.md` —— 路线图与备忘。

### 致谢

手柄插图源自 CREATRBOI 的 "White XBOX Controller" 模型；许可证与署名文件随应用分发（Release 内 `THIRD-PARTY/` 目录）。
