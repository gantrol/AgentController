# Codex Micro：Plan request-card 的 AG00 返回缺口

## 状态

- 已验证版本：Codex `26.715.10079`
- 影响对象：实体 Codex Micro 的 Agent 0；未安装本项目兼容层的同类 HID 输入
- 不受影响：旋钮中心短按确认；普通 menu／listbox 中由 AG00 触发的返回

Codex 当前会在普通菜单或 listbox 打开时把 AG00 解释为 Escape，但 Plan 模式的
request-navigation 卡片不在该判断范围内。卡片自身支持 Escape，因此问题是桥的
上下文覆盖缺口，不是确认键或卡片的 Escape 行为损坏。

本文中的“取消”专指卡片右上角 Dismiss/X，不等同于 Skip。

## 复现

1. 在 Codex `26.715.10079` 中进入 Plan 模式，并让前台会话显示带 Dismiss、Skip
   与选项／自由输入的提问卡片。
2. 转动 Micro 旋钮选择选项；中心短按可以确认。
3. 按实体 Micro 的 Agent 0。卡片不会按右上角 Dismiss/X 的语义关闭，且可能执行
   普通槽位切换。
4. 键盘按 Escape，卡片可以关闭。

## 本项目的窄范围兼容层

虚拟 Micro 的 Agent 0 与 AgentController 的返回手势共用
`CodexRequestCardCancellation.TryCancelForegroundRequestCard()`：

- 只读检查前台 Codex 的可见 Group；优先要求 class 包含
  `@container/request-card`，并同时存在 Dismiss、Skip、RadioButton／Edit 结构；
- 发送前重新核对前台 HWND 和进程；成功时只发送一次 Escape 按下／释放，不调用
  UIA Invoke，也不追加 AG00；
- `NotPresent` 才回落原有 AG00；标记变化、候选不唯一、元素失效、非 Codex 前台
  或 SendInput 失败都会消费本次动作，避免误切任务；
- 本地不修改 Codex 安装包。实体 Micro 的输入无法由本进程拦截，仍等待上游修复。

## 移除条件

当受支持的 Codex 版本已经在 request-navigation 卡片中把 AG00 官方映射为
Dismiss/Escape，并通过“卡片仅关闭一次、无 Agent 切换、普通菜单仍可返回、无卡片
仍切换 Agent 0”的实测后，应删除共享兼容层和版本说明，恢复完全由
Micro bridge 持有该上下文语义。

官方交互基线：[Codex Micro](https://learn.chatgpt.com/docs/features/codex-micro)。
