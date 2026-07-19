# 手柄操作列表

> Status: Product interaction contract
> 这份表描述目标交互，不代表所有路径已经通过当前 Codex build 的真机验收。

## 1. 基础层

| 输入 | 操作 | 首选通道 |
| --- | --- | --- |
| Menu / Start / `+` | 启动 Codex，或将已运行的 Codex 置于前台 | Window/Agent adapter |
| 左摇杆上/下 | 在同级任务或项目条目间移动 | 本地任务导航 |
| 左摇杆右/左 | 进入项目 / 退出项目 | 本地任务导航 |
| L3 | 循环置顶任务、置顶项目、项目、未归项目任务 | 本地任务导航 |
| A | 打开当前任务或确认当前本地选择 | App Server / deeplink / verified adapter |
| 右摇杆上/下 | 按视觉顺序遍历 Composer 控件或菜单项 | Micro `ENC_CW / ENC_CC` |
| 右摇杆左/右 | 按屏幕方向调整当前控件；右进入、左返回 | 独立 `CurrentControlLeft/Right` executor；不得发送 `ENC_*` |
| R3 短按 | 打开、进入或确认当前 Micro 旋钮项 | Micro `ENC` down/up |
| R3 长按 | 打开 Agent Controller 设置 | 本地应用动作；不得泄漏成方向输入 |
| LT 按住/松开 | 开始/停止按住说话 | Micro `ACT10` down/up |
| X | 发送当前 composer 内容 | 已验证 Command slot；默认 `ACT12`，否则语义 adapter |
| B 短按 | 关闭当前菜单、返回或撤回最近导航 | 当前上下文 Cancel |
| B 长按 3 秒 | 终止当前运行；提前松开取消 | App Server `turn/interrupt` 优先 |
| Y | 打开动作面板 | 本地 UI |
| 十字键上/下 | 上一轮/下一轮；长按到顶部/底部 | Codex semantic/verified navigation adapter |
| LB/RB 短按 | 上一个/下一个可用任务 | 本地任务导航 |

L3/R3 都表示**垂直按下摇杆帽**，不是把摇杆向下拨。

## 2. Y 动作面板

| Y 后输入 | 操作 |
| --- | --- |
| 十字键上 | 新建任务 |
| 十字键右/左 | Codex 历史前进/后退 |
| 十字键下 | 显示/隐藏 Codex 侧边栏 |
| A，再按一次 A | 二次确认后清空输入框 |
| X | 项目上下文：进入所属项目，或切换全部/仅置顶 |
| B 或 Y | 关闭动作面板 |

## 3. 按住组合层

| 层 | 输入与操作 | 首选通道 |
| --- | --- | --- |
| LB — Agent | 十字键上/右/下/左选择槽 1–4；View 选择槽 5；Menu 选择槽 6；B 取消 | Micro `AG00..AG05`；任意任务树不伪造 Agent slot |
| RB — Command | Y Fast；A Approve；B Decline；X Fork；View PTT；Menu Dispatch | 当前布局中已验证的 `ACT*` Command slot |
| RT — Running | X Steer；Y Queue；B 长按 3 秒 Stop；A Fork | App Server/语义 adapter；只在已验证运行上下文开放 |

## 4. 右摇杆固定合同

| 物理方向 | 含义 | 禁止行为 |
| --- | --- | --- |
| 上 | 选择上一控件/菜单项，发送一个 `ENC_CW` 档位 | 不得执行横向增加或进入 |
| 下 | 选择下一控件/菜单项，发送一个 `ENC_CC` 档位 | 不得执行横向减少或返回 |
| 左 | 当前控件向左/减小，或退出当前菜单 | 不得发送 `ENC_CW/ENC_CC` |
| 右 | 当前控件向右/增大，或进入当前菜单 | 不得发送 `ENC_CW/ENC_CC` |
| 回中 | 结束当前轴手势并释放 ownership | neutral 不得被合并丢失 |

一次手势只能由一个轴持有；必须回中后才能把 ownership 切换给另一个轴。菜单打开与否不能改变轴语义。

## 5. 安全与结果规则

- Bridge 关闭时，手柄不得对 Codex 产生动作，只允许被动显示与安全释放。
- Approval、Decline、Stop、Steer 等高风险动作必须先确认上下文和目标任务。
- transport `Accepted` 不是业务完成；必须用可见状态、App Server 事件或其他 readback 验证结果。
- 只有明确 `NotSent` 才能走第二通道；`OutcomeUnknown` 后禁止自动重试非幂等动作。
- 手柄断连、进程退出或 Broker lease 丢失时，补发 PTT release 和 Analog neutral，但不能释放其他客户端仍持有的状态。
