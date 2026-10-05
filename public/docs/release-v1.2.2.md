# Agent Controller v1.2.2

Release candidate; not yet published. Windows file/Store version: `1.2.2.0`.

## Changes

- Adopt GPL-3.0-only for project-owned code and distribution metadata. Preserve third-party artwork licenses and attribution.
- Include the current software-interface control path and controller navigation/shell updates accumulated after `1.2.1`.
- Pin `CodexControl` and `CodexControl.Windows` to `1.0.0`, distributed with matching source.
- Prepare the legacy DeepSeek Harness bridge `0.2.9` with GPL licensing. This metadata release does not renew its compatibility claim: the integration was last maintained against Harness `0.0.3`.

## Artifacts

- `AgentController-1.2.2-win-x64.zip`: self-contained Windows x64 build.
- `AgentController-1.2.2-win-x64-compact.zip`: requires .NET 10 Desktop Runtime x64.
- Corresponding Agent Controller and shared-control source archives, exact control packages and SHA-256 checksums.
- Store MSIX candidates are prepared separately and require the verified product identity; they are not automatically submitted.

## Verification boundary

Existing isolated tests and Release builds are the automated release checks. Physical-controller-to-Codex acceptance, fresh installation, startup behavior under Store identity and current DeepSeek compatibility remain separate checks. Do not infer those results from a successful build. Codex integration follows version-specific private interfaces that may change.

## 简体中文

`1.2.2` 是待发布版本，尚未公开发布。自有代码统一为 GPL-3.0-only，第三方手柄素材继续遵守原有许可与署名要求。包含 `1.2.1` 之后积累的软件接口控制、导航及窗口行为更新，共享控制包固定为 `1.0.0`。

提供 Windows x64 自包含包、需要 .NET 10 Desktop Runtime 的精简包，以及对应源码、依赖包和校验文件。旧 DeepSeek 桥接插件 `0.2.9` 仅同步本次许可发布，不代表恢复对新版 Harness 的兼容维护。

现有自动化测试与 Release 构建不替代真实手柄、Codex、全新安装和商店身份下的验收。准备商店包不代表已经提交或审核通过。
