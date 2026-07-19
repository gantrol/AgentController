Codex Micro Simulator v1.0.1 - Windows x64 portable application
================================================================

This archive contains the self-contained desktop application only. It does not
install or trust a self-signed driver certificate and does not require a
separate .NET runtime.

Install the companion CodexMicroVhfUm v1.0.0 developer driver before running
CodexMicroSimulator.exe. The driver package is intentionally unsigned and must
be audited and signed locally for development or testing use.

English guide:
https://github.com/gantrol/AgentController/blob/main/virtual-micro/UNSIGNED-DRIVER.md

简体中文说明：
https://github.com/gantrol/AgentController/blob/main/virtual-micro/UNSIGNED-DRIVER.zh-CN.md

Run:
1. Finish the driver signing and installation described above.
2. Extract this entire archive to a normal writable directory.
3. Run CodexMicroSimulator.exe as a normal, non-administrator user.

Codex builds not yet present in the reviewed fingerprint list are indicated by
the first yellow status LED and may continue in compatibility mode. A known
build whose reviewed files no longer match is still blocked.

The screen simulator retains its most recent valid Agent lighting when Codex
sends the physical device's inactivity all-off frame. If Codex is already idle
at startup, a neutral blue screen-only fallback remains illuminated. This does
not change the HID protocol or connection state.

This independent experiment is not affiliated with, authorized by, or endorsed
by OpenAI, Codex, or Work Louder.
