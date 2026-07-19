# 07 — 安全、发行与商业化

> Status: Planned
> Priority: P0 / Ongoing
> Depends on: All feature tracks

## 目标

让“控制另一个 Agent 应用”的高权限能力可解释、最小化、可审计、可回滚，并建立可持续的签名发行和商业授权基础。

## 待办

### 威胁模型

- [ ] 建立资产、信任边界、攻击面和滥用场景清单。
- [ ] 单独评审 Submit、Approve、Decline、Stop、Shell/Skill 等高风险动作。
- [ ] 定义 Bridge off、前台限制、断连、锁屏和用户切换时的强制行为。
- [ ] Native Broker 使用当前用户 ACL、固定 schema、大小上限和单客户端策略。
- [ ] 禁止桌面 UI 向驱动传递任意 report 或任意内核操作。

### 隐私与诊断

- [ ] 默认不上传 prompt、任务标题、文件内容、账户标识或原始协议 payload。
- [ ] 诊断导出支持本地预览和字段脱敏。
- [ ] 任何遥测均显式 opt-in、可关闭、可删除并有公开 schema。
- [ ] 更新 README 安全声明和权限说明，使其与真实行为一致。

### 供应链与构建

- [ ] 固定依赖版本，启用漏洞和许可证扫描。
- [ ] 生成 SBOM、SHA-256 和可验证发布清单。
- [ ] Windows 可执行文件和驱动采用独立签名策略。
- [ ] macOS 应用、helper、entitlement 和公证产物进入自动验证。
- [ ] Updater 使用签名清单，失败时保留可回滚版本。

### Windows 驱动发行

正式用户不应安装 WDK、运行 PowerShell/Inf2Cat/SignTool/PnPUtil、自行编译驱动或信任项目自签名根证书。源码构建和测试证书只属于隔离开发/受控测试；完整产品模式由发布者提供 Microsoft-signed Device Support。

架构与安装事务详见 [ADR-0002](../docs/adr/0002-codex-micro-native-compatibility.zh-CN.md)。驱动虽然允许按需安装，但它是 Windows Full Micro mode 的硬依赖；无驱动只能进入明确标记的 Limited mode，不能继续扩建一套同等地位的 UIA 仿制实现。

- [ ] 先通过设备身份 Gate：取得目标 VID/PID/兼容身份的书面可发行依据，或获得 Codex 对项目自有 VID/PID 的官方 allowlist。
- [ ] 将 KMDF/VHF 冻结为唯一正式候选；UMDF2/VHF 只做 HLK/微软支持确认实验，直接 UMDF HID minidriver 只在 KMDF 路线失败时评估。
- [ ] 冻结项目唯一 root Hardware ID、interface GUID、Provider、DriverVer、x64/ARM64 INF、升级排名、回滚与卸载所有权规则。
- [ ] 建立可复现 CI：输出 INF、CAT、SYS、PDB/symbol、版本信息、SHA-256、SBOM、签名 manifest 和安装清单；用户机器不参与编译或制包。
- [ ] 取得 EV 代码签名证书并注册 Windows Hardware Developer Program；正式包通过 HLK/WHCP 后由 Hardware Dev Center 签名。
- [ ] attestation/preproduction 只用于受控测试，不作为 Windows Update 零售包或“已经认证”的宣传依据。
- [ ] 桌面应用、Broker、安装器和 driver package 分别按职责签名；UAC 的 verified publisher 来自安装器签名，driver 信任来自 Microsoft 返回包。
- [ ] Device Support 固定支持 `status/install/update/repair/uninstall/diagnose`，不接受任意 INF、Hardware ID、服务名或删除目标，并返回 versioned JSON、明确退出码和 `rebootRequired`。
- [ ] 普通 Install/Update 不使用 `INSTALLFLAG_FORCE`；Repair 只有在签名者、Hardware ID、新旧版本和安装记录均确认属于本产品时才允许受控强制重装。
- [ ] 安装后验证 root devnode、published INF/signature、VHF child descriptor、Broker ping、双向 reports、最小 RPC 和 Codex 实际打开设备；Windows 枚举成功本身不算产品 Ready。
- [ ] 新装失败删除刚创建的 devnode；升级失败恢复旧签名包；成功后才精确删除安装记录确认归属本产品的旧 INF。
- [ ] 在干净 Windows 10/11、x64/ARM64、Secure Boot、Memory Integrity/HVCI、Driver Verifier、睡眠/唤醒、升级/降级/重启与 OS feature update 矩阵中验证。
- [ ] 提供企业离线包、静默参数、签名者/Hardware ID allowlist 和脱敏诊断；策略阻止时报告 `BlockedByPolicy`，绝不尝试关闭 WDAC、Secure Boot 或 Memory Integrity。
- [ ] 普通 app/Broker 兼容更新无 UAC；仅 driver 版本变化、Repair 和 Uninstall 提权。后续 driver 更新优先使用可灰度、可监控、可回滚的 Windows Update 通道。

依据：[微软 VHF 总览](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/virtual-hid-framework--vhf-)、[驱动签名选项](https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/driver-signing-offerings)、[安全部署建议](https://learn.microsoft.com/en-us/windows-hardware/drivers/develop/safe-deployment-best-practices-for-drivers)、[Partner Center for Windows Hardware](https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/)。

### 法律与品牌

- [ ] 明确 Agent Controller、Codex、Codex Micro、Work Louder 的品牌边界。
- [ ] 商业发行不使用未经书面许可的 VID/PID、设备身份或私有包代码；购买自有 VID 不能替代 Codex detection allowlist。
- [ ] 对“虚拟 HID 是否落入 USB-IF 认证范围”和“兼容性描述是否构成第三方产品冒用”取得专业法律意见；在此之前按更严格边界执行。
- [ ] 对 PolyForm Noncommercial、商业授权和第三方贡献做专业法律审查。
- [ ] 保留独立项目、不隶属、不背书的清晰声明。

### 商业交付

- [ ] 定义 Community、签名发行、Team/Commercial 和定制支持的边界。
- [ ] 团队版只增加真正的团队价值：部署、策略、审计、支持和兼容保障。
- [ ] 建立支持矩阵、响应级别和兼容版本生命周期。
- [ ] 不在没有持续服务价值时强推订阅。

## 完成门槛

- 安全审查覆盖所有平台和 Native 组件。
- 正式包签名、可验证、可升级、可卸载和可回滚。
- 用户能在执行前理解权限、风险和实际执行通道。
- 许可证、商标、设备身份和商业授权不存在未决的发行阻断项。
