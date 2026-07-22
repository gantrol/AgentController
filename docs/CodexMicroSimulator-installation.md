# Agent Controller local installation (including the Micro driver)

[简体中文](./CodexMicroSimulator-安装教程.zh-CN.md)

For Windows 10/11 x64. The driver only provides the HID channel shared by a physical controller and the virtual Micro surface. It **does not read or check Codex or Agent Controller versions**. The INF version only lets Windows order driver packages; it is not an application compatibility allowlist.

## Quick install

1. Extract the Agent Controller application and Micro driver packages, then exit any older Agent Controller process.
2. In an ordinary PowerShell, run this from the repository root:

   ```powershell
   .\virtual-micro\Install-CodexMicroDriver.ps1
   ```

   If the current directory is the extracted `virtual-micro` directory, run:

   ```powershell
   .\Install-CodexMicroDriver.ps1
   ```

3. Approve the Windows UAC prompt opened by the script. A final `Ready` message means installation succeeded.
4. Start `AgentController.exe` as a normal user. Click the small keyboard icon in the title bar to open Micro. Micro is always on top by default; right-click an empty part of its body to toggle that setting.

Do not keep Agent Controller elevated, and do not disable Windows driver-signing enforcement.

## Upgrade

- For a normal Agent Controller or Codex update, exit Agent Controller, replace the application files, and restart it.
- **Do not reinstall the driver for every update.** Run the installer again only when the driver package itself changes, the device disappears, or its health check fails.
- The driver version does not need to match either Codex or Agent Controller.

## Verify and troubleshoot

The install script ends with `Ready` when successful. You can also check the device directly:

```powershell
Get-PnpDevice -FriendlyName 'Codex Micro Simulator UMDF2 Virtual HID' |
  Select-Object Status, FriendlyName, InstanceId
```

There should be one device and its `Status` should be `OK`. If installation fails, read `virtual-micro/driver-install.log` first. See [`UNSIGNED-DRIVER.md`](../virtual-micro/UNSIGNED-DRIVER.md) for full build, re-signing, and certificate details.

## Appendix: prerequisites

### Running the release package only

- Windows 10/11 x64;
- the Codex desktop app installed and signed in;
- a Windows-recognized XInput controller for physical-controller input, and a working microphone for the voice key;
- no separate .NET Runtime installation—the Agent Controller Windows release is self-contained.

### Locally signing and installing the prebuilt driver

- Windows SDK `10.0.26100.0` for SignTool;
- pinned NuGet package `Microsoft.Windows.WDK.x64` `10.0.26100.6584` for Inf2Cat;
- Visual Studio/Build Tools MSBuild for one online restore when that NuGet package is not already cached.

This route does not require a C++ compiler, the Visual C++ Redistributable, or a .NET Runtime.

### Rebuilding from source

Also install Visual Studio Build Tools 2022 with **Desktop development with C++**, MSVC v143 x64/x86, x64/x86 Spectre-mitigated libraries, and MSBuild. Building Agent Controller also requires .NET SDK 10.

UAC is needed only while installing the driver. Do not import certificates from an untrusted source or distribute the local test certificate/private key created by the script.
