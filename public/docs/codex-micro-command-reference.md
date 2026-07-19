# Codex Micro 指令参考

> Status: Observed private compatibility contract
> Evidence baseline: Codex Desktop 26.707.12708 and local `codex-micro-v0.1.0`
> 这些值不是 OpenAI 承诺的公开稳定 ABI；使用前必须经过 Codex build 指纹门禁。

## 1. `v.oai.hid` 键指令

消息形状：

```json
{"m":"v.oai.hid","p":{"k":"ENC_CW","act":2}}
```

设备发给 Codex 的完整 JSON 消息以 LF（`\n`）结束。

### `act` 值

| `act` | 含义 | 典型用途 |
| --- | --- | --- |
| `0` | key up / release | 普通键、Agent key、PTT 和 `ENC` 释放 |
| `1` | key down / press | 普通键、Agent key、PTT 和 `ENC` 按下 |
| `2` | 离散旋钮档位 | `ENC_CW`、`ENC_CC`；每个档位一条通知 |

### 旋钮键

| `k` | 指令 | Agent Controller 映射 |
| --- | --- | --- |
| `ENC_CW` | 编码器顺时针一个档位，`act=2` | 右摇杆上 / `EncoderStep(+1)` |
| `ENC_CC` | 编码器逆时针一个档位，`act=2` | 右摇杆下 / `EncoderStep(-1)` |
| `ENC` | 编码器按压，使用 `act=1` 后 `act=0` | R3 短按；Agent Controller 的 R3 长按保留给自身设置，不发送 `ENC` hold |

右摇杆左/右不是 Micro 旋钮档位，禁止编码成 `ENC_CW` 或 `ENC_CC`。

### Agent 与 Command key

| `k` | 当前观测/默认含义 | 正确时序 |
| --- | --- | --- |
| `AG00..AG05` | 六个 Agent slot | tap：`1` → `0`；双击由两组 tap 构成 |
| `ACT06` | Fast toggle | tap：`1` → `0` |
| `ACT07` | Approve | tap：`1` → `0`；必须先验证审批上下文和布局 |
| `ACT08` | Decline | tap：`1` → `0`；必须先验证审批上下文和布局 |
| `ACT09` | Fork | tap：`1` → `0` |
| `ACT10` | Push-to-talk | 按住时 `1`，松开时 `0`；不能简化成 tap |
| `ACT10_ACT11` | Codex layout 中的组合键帽/命令槽标识 | 当前 renderer 投影到 `ACT10`，避免重复动作 |
| `ACT12` | Codex/Submit | tap：`1` → `0`；只有当前 layout 仍映射 `composer.submit` 时可发送 |

`ACT*` 的业务含义可由用户在 Codex 中修改。应用必须只读验证当前 layout，不能仅凭默认表猜测。

## 2. Analog 指令

消息形状：

```json
{"m":"v.oai.rad","p":{"a":0.75,"d":1}}
```

| 字段 | 含义 |
| --- | --- |
| `a` | 归一化角度，范围 `0..1` |
| `d` | 归一化距离，范围 `0..1`；`0` 表示 neutral |

一次离散 Analog pulse 至少包含：

```text
center:    d=0
direction: a=<angle>, d=>0
release:   same angle, d=0
```

允许合并中间移动帧，但不能丢失最后的 neutral。

## 3. Device RPC

Codex → 设备的最小请求面：

| method | 用途 | 设备响应 |
| --- | --- | --- |
| `sys.version` | 查询固件/兼容版本 | `{ "version": "..." }` |
| `device.status` | 查询 profile、layer、电量和充电状态 | `{ "version", "profile_index", "layer_index", "battery", "is_charging" }` |
| `v.oai.rgbcfg` | 灯光配置 | 复用请求 id，返回 `result: true` 或明确 error |
| `v.oai.thstatus` | 六个 Agent slot 状态/灯光 | 重组状态、复用请求 id，并发布槽位快照 |

RPC request/response 使用 `id` 关联，每个请求必须恰好响应一次。Codex → 设备的 output 分片没有 LF；设备 → Codex 的 notification/response 必须以 LF 结束，两侧不能共用同一终止判断。

## 4. HID report

| 字段 | 值 |
| --- | --- |
| VID / PID（观测设备身份） | `0x303A / 0x8360` |
| Usage Page / Usage | `0xFF00 / 0x0001` |
| Report ID | `0x06` |
| RPC / Debug channel | `0x02 / 0x01` |
| Report 总长度 | 64 bytes，含 Report ID |
| 单 report payload | 最多 61 bytes UTF-8 |
| 完整消息上限 | 64 KiB |

```text
byte 0      report id = 0x06
byte 1      channel = 0x02
byte 2      payload length = 0..61
byte 3..63  UTF-8 payload chunk；剩余补零
```

UTF-8 多字节标量不能在两个 input report 之间拆开。

## 5. 传输结果

| 结果 | 含义 | 是否允许 fallback |
| --- | --- | --- |
| `NotSent` | 确认没有 report 进入设备 | 是；可进入预先定义的降级路径 |
| `Accepted` | 驱动接受了完整 batch | 否；仍需业务 readback |
| `OutcomeUnknown` | 可能已部分或全部送达，结果未知 | 否；禁止自动重发非幂等动作 |
| `Rejected` | 驱动明确拒绝 batch | 否；报告错误并修复兼容/状态问题 |

transport ACK 不是 `v.oai.hid` 的动作完成回执。

## 6. 常用时序

```text
右摇杆上: ENC_CW act=2
右摇杆下: ENC_CC act=2
R3 短按:  ENC act=1 -> ENC act=0
PTT:      ACT10 act=1 -> 保持 -> ACT10 act=0
Agent 1:  AG00 act=1 -> AG00 act=0
Submit:   ACT12 act=1 -> ACT12 act=0
```

## 7. UMDF2/VHF 驱动合同

当前公开实现只选择 `CodexMicroVhfUm.dll`：

- UMDF2 HID source driver，调用系统 VHF；
- Source PnP ID：`Root\CodexMicroHidUm`；
- 私有接口：`E2A7CB54-8420-4D51-9DD8-D6575B9251D1`；
- contract magic/version：`0x314D4356` / `1`；
- 唯一的 `AgentController.MicroBroker` 使用 `GET_INFO`、`SUBMIT_INPUT`、`READ_OUTPUT`，并为模拟器的受限对话框操作使用 `SUBMIT_KEYBOARD`；
- `codex-micro-v0.1.0` 的同一 UMDF2 source 提供受限键盘 child，只允许 Tab、Shift+Tab、Enter。

Agent Controller 与 `virtual-micro` 都没有静态链接或直接打开该 DLL；它们通过当前用户 named pipe 连接 Broker。只有 Broker 通过设备接口使用合同、分配全局 batch sequence 并读取 output/RPC。架构测试禁止桌面客户端重新出现 `DeviceIoControl` 或私有接口 GUID。发布与健康检查仍需验证实际安装的 Provider、service、PnP identity、版本和签名，不能只凭 GUID 判断驱动身份。
