# 09 — 渐进迁移与兼容策略

> Status: In Progress
> Priority: P0
> Depends on: 01–08

## 目标

在不中断 v0.7 用户的前提下，将现有 Windows WPF 原型逐步迁移到跨平台架构，避免一次性重写造成长期不可发布状态。

## 迁移阶段

### M0：冻结基线

- [ ] 为当前 v0.7 打标签并记录完整测试、设置 schema 和支持矩阵。
- [ ] 建立关键动作行为合同，特别是 Submit、Stop、Fork、PTT 和 Agent slots。
- [ ] 保留当前未完成的实机项目在 `90-v0.7-maintenance.md`。

### M1：抽核心，不改行为

- [x] 引入 Domain/Application/Platform.Abstractions。
- [ ] 现有 WPF 引用新核心，用户可见行为保持一致；open/create/fork/submit/clear/stop、shell navigation/sidebar、会话短按/长按、routine UI command 与双确认 Approve 自动化回归已通过，README/动作面板实机步骤待复验。
- [x] 按可回滚动作链迁移并删除旧直接路径；Composer 同通道动作复用 executor，Fork 的 Micro/快捷键/UIA 回退则封装为独立 adapter policy。

### M2：替换权威通道

- [ ] App Server 完成第一个 Thread/Turn 垂直切片。
- [ ] Micro 进入独立协议项目并启用兼容性门禁。
- [ ] UIA/快捷键被标为 Companion fallback，不再冒充统一业务接口。

### M3：自定义映射

- [ ] 新 binding schema 与旧设置并行读取。
- [ ] 提供 dry-run 迁移报告和可回滚备份。
- [ ] 新设置成功保存后才提高 schema version。

### M4：Avalonia Windows 等价

- [ ] 新旧客户端可并行安装但不同时占用控制器/Bridge。
- [ ] 建立页面、动作、托盘、Overlay、权限和设置等价矩阵。
- [ ] 达到等价后将 WPF 进入只读维护期。

### M5：macOS MVP

- [ ] 先交付无虚拟 HID 的核心工作流。
- [ ] 平台差异通过 ports 解决，不向 Domain 加 `if macOS`。

### M6：可选 Native 组件

- [ ] Windows VHF 和 macOS 虚拟 HID 使用独立版本与发行通道。
- [ ] 安装失败或组件缺失不影响主应用核心功能。
- [ ] 只有通过兼容、签名、安全和法律门禁才进入 Stable。

## 兼容原则

- 设置迁移必须幂等、带版本、原子写入并可恢复。
- 未知字段默认保留，未知枚举安全降级。
- 未知 Codex build 禁用私有兼容层，不阻断官方 App Server。
- 新旧组件不能同时执行同一非幂等动作。
- 每个阶段都有独立可发布产物，不维持长期“全部未完成”的大分支。

## 完成门槛

- 新架构覆盖所有保留的核心使用场景。
- WPF 退役前已有设置迁移、回滚和用户沟通方案。
- Windows/macOS 使用同一 Domain/Application 合同。
- Experimental Native Components 可独立安装、升级、禁用和卸载。
