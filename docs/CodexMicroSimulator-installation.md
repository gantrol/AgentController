# Codex Micro driver installation

[简体中文](./CodexMicroSimulator-安装教程.zh-CN.md)

For Windows 10/11 x64. This driver provides the HID channel used by a physical
controller and the virtual Micro surface. It **does not read or check the Codex
or AgentController version**. The INF driver version exists only so Windows can
identify and update the driver package; it is not a compatibility allowlist.

## Install

Exit AgentController first. From the repository root or extracted driver
package, open PowerShell and run:

```powershell
.\virtual-micro\Install-CodexMicroDriver.ps1
```

When already inside the extracted `virtual-micro` directory, run:

```powershell
.\Install-CodexMicroDriver.ps1
```

The script opens the Windows UAC prompt itself. Approve it to complete local
signing, installation/update, and the device-health check. You do not need to
open an elevated PowerShell first. Do not disable Windows driver-signing
enforcement.

## Verify

A successful run ends with `Ready`. You can also run:

```powershell
Get-CimInstance Win32_PnPSignedDriver |
  Where-Object DeviceName -eq 'Codex Micro Simulator UMDF2 Virtual HID' |
  Select-Object DeviceName, DriverVersion, InfName

Get-PnpDevice -FriendlyName 'Codex Micro Simulator UMDF2 Virtual HID'
```

There should be one device and its `Status` should be `OK`. `DriverVersion` is
a Windows package version; it does not have to match AgentController or Codex.

Run AgentController as a normal user, not as Administrator. Controller input
starts enabled on every launch. The top switch pauses only the current session;
exiting the app fully disables controller handling.

## If installation fails

Read `virtual-micro/driver-install.log` first. The most common cause is a
package missing SignTool/Inf2Cat or the built driver artifacts. See
[`UNSIGNED-DRIVER.md`](../virtual-micro/UNSIGNED-DRIVER.md) for the complete
build, re-signing, and certificate workflow.

Do not import certificates from an untrusted source or distribute the local
test certificate/private key created by the script.
