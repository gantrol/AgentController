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

正式用户不应安装 WDK、运行 Inf2Cat/SignTool 或自行编译驱动。源码构建与测试证书只属于开发/明确选择加入的测试通道。

- [ ] 先统一 `virtual-micro/` 的 KMDF/UMDF2 实现、INF、硬件 ID、安装脚本和文档，再冻结待认证的唯一驱动包。
- [ ] 建立可复现 CI：输出 INF、CAT、SYS/DLL、版本信息、SHA-256、SBOM 和安装清单；用户机器不参与编译或制包。
- [ ] 取得 EV 代码签名证书并注册 Windows Hardware Developer Program；正式包经 Hardware Dev Center 签名。
- [ ] 将 HLK/WHQL 作为面向普通用户和 Windows Update 分发的目标；attestation/test 包只用于受控验证，不作为默认零售通道。
- [ ] 桌面应用与驱动分别签名；提供最小权限的提权安装器、显式授权、安装前兼容性检查、健康检查、精确卸载和失败回滚。
- [ ] 在干净 Windows 10/11、Secure Boot 开/关、Memory Integrity/HVCI 开/关、升级/降级/重启矩阵中验证，无 WDK/Visual Studio 依赖。
- [ ] 驱动未达到签名发行门槛时保持 Experimental/可选；无驱动时 App Server 和桌面自动化主路径仍完整可用。
- [ ] 发布说明明确区分 Developer test package 与 Microsoft-signed release package，禁止引导普通用户信任项目自签名根证书或修改启动安全策略。

依据：[微软 VHF 总览](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/virtual-hid-framework--vhf-)、[公开发行签名](https://learn.microsoft.com/en-us/windows-hardware/drivers/develop/signing-a-driver-for-public-release)、[Partner Center for Windows Hardware](https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/)。

### 法律与品牌

- [ ] 明确 Agent Controller、Codex、Codex Micro、Work Louder 的品牌边界。
- [ ] 商业发行不使用未经许可的 VID/PID 或私有包代码。
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
