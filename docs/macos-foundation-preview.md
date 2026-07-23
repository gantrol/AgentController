# macOS Foundation Preview

[简体中文](./macos-foundation-preview.zh-CN.md)

[Cross-machine physical Mac test handoff (简体中文)](./macos-test-handoff.zh-CN.md)

This is the roadmap's runnable macOS foundation, not a port claiming parity
with Windows Full Micro mode. It validates the shared desktop shell, Apple Game
Controller, permission presentation, and the local Codex CLI environment. It
does not install Windows VHF or present Accessibility as a Micro substitute.

## Included

- Avalonia 12.1 / .NET 10 shared shell, targeting macOS 14 or later;
- native menu bar, Dock menu, and process-level single instance;
- Apple Game Controller extended profiles, multiple/current controllers,
  session-stable identities, topology revisions, sticks, buttons, triggers,
  battery, haptics, and light capability observation;
- separate Accessibility, Input Monitoring, and Microphone status rows plus a
  Privacy & Security settings entry;
- Codex CLI path detection;
- self-contained Apple Silicon (`osx-arm64`) and Intel (`osx-x64`) `.app`
  bundles.

## Explicitly not included

- a Codex App Server Thread/Turn action client;
- production dictation, Submit, Steer/Queue/Stop, or other semantic actions;
- CoreHID virtual Micro, HIDDriverKit/System Extension, or related entitlement;
- Developer ID signing, notarization, DMG/PKG, or automatic updates.

The current UI is read-only and sends no action to Codex. It remains visibly
labelled `LIMITED PREVIEW` until physical Mac, signing, and security acceptance
is complete.

The controller panel keeps the last connection, disconnection, or
current-controller transition visible. Array reordering does not change a
controller's process-session identity. Background monitoring is shown as
available only when `shouldMonitorBackgroundEvents` reads back as enabled.

## Build both app bundles

From the repository root:

```powershell
dotnet restore .\AgentController.sln
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-macos.ps1
```

On macOS with PowerShell 7, use `pwsh -NoProfile -File
./scripts/publish-macos.ps1 -Runtime osx-arm64` (or `osx-x64`).

Outputs:

```text
artifacts/macos/osx-arm64/Agent Controller.app
artifacts/macos/osx-x64/Agent Controller.app
```

The script validates the apphost's Mach-O CPU type, `Info.plist`, and
`libAvaloniaNative.dylib`. Windows cannot preserve the Unix executable bit.
After copying a bundle to a Mac, run from its architecture directory:

```bash
chmod +x 'Agent Controller.app/Contents/MacOS/AgentController.Desktop'
open 'Agent Controller.app'
```

Do not disable Gatekeeper. Public distribution requires correct per-binary
Developer ID signing with hardened runtime and
`packaging/macos/FoundationPreview.entitlements`, followed by notarization and
stapling on a Mac. See the
[Avalonia macOS deployment guide](https://docs.avaloniaui.net/docs/deployment/macos).

## Physical acceptance checklist

- [ ] launch on macOS 14, 15, and 26;
- [ ] run the Apple Silicon and Intel bundles on matching hardware;
- [ ] validate Xbox, DualSense, 8BitDo, and generic connection/disconnection,
  multiple controllers, and current-controller changes;
- [ ] validate background and sleep/wake behavior without held or stale state;
- [ ] validate menu bar, Dock menu, single instance, and exit lifecycle;
- [ ] verify the preview never prompts for Microphone or Input Monitoring;
- [ ] validate Developer ID signing, notarization, and Gatekeeper in a clean
  user account.

The Windows cross-build proves project/XAML compilation, both Mach-O apphosts,
and the Avalonia native library layout. It cannot replace physical Mac
acceptance.
