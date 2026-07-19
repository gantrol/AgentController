# Codex Micro Simulator installation and validation guide

[简体中文](./CodexMicroSimulator-安装教程.zh-CN.md)

## Scope

This guide applies to Windows 10/11 x64 and the following two extracted directories:

- `CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER`: the unsigned developer driver package;
- `CodexMicroSimulator-v1.0.0-win-x64-portable-compatible`: the self-contained portable simulator, which does not require a separate .NET Runtime.

The driver package is not a production-signed installer. Its installation script creates a non-exportable local self-signed code-signing certificate, adds the public certificate to the machine's `Root` and `TrustedPublisher` stores, signs the UMDF2 DLL, regenerates and signs the catalog, and then installs the virtual HID device. Use it only on a development or test computer after verifying the source and package contents.

## 1. Prepare the environment

Requirements:

- administrator privileges;
- `signtool.exe` from Windows SDK `10.0.26100.0`;
- `Inf2Cat.exe` from the pinned WDK NuGet package `Microsoft.Windows.WDK.x64` `10.0.26100.6584`;
- the Codex Windows application, installed and signed in.

Default tool locations:

```text
C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe
%USERPROFILE%\.nuget\packages\microsoft.windows.wdk.x64\10.0.26100.6584\c\bin\10.0.26100.0\x86\Inf2Cat.exe
```

The driver package includes `SHA256SUMS.txt`. Before installation, use `Get-FileHash -Algorithm SHA256` to compare every packaged file with the manifest. The recorded v1 package validation checked 20 entries with zero mismatches.

## 2. Install the virtual HID driver

Open PowerShell as Administrator, enter the extracted driver-package directory, and run:

```powershell
cd "$env:USERPROFILE\Downloads\CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER"
.\Install-CodexMicroDriver.ps1
```

The script performs these steps in order:

1. Create or reuse the local `CN=Codex Micro Simulator Driver` certificate.
2. Add its public certificate to the machine's trusted root and trusted publisher stores.
3. Sign `CodexMicroVhfUm.dll`.
4. Remove the old catalog and regenerate it with Inf2Cat.
5. Sign the new catalog.
6. Install or refresh `Root\CodexMicroHidUm`.
7. Check the device's PnP health state.

The installation log is written to `driver-install.log` in the driver-package root.

## 3. Verify the driver

Run the following in PowerShell:

```powershell
$device = Get-CimInstance Win32_PnPEntity |
    Where-Object { @($_.HardwareID) -contains 'Root\CodexMicroHidUm' }

$device | Select-Object Name, Status, ConfigManagerErrorCode, PNPDeviceID

pnputil /enum-interfaces /class '{E2A7CB54-8420-4D51-9DD8-D6575B9251D1}'
```

A healthy installation has all of the following:

- device name: `Codex Micro Simulator UMDF2 Virtual HID`;
- `Status`: `OK`;
- `ConfigManagerErrorCode`: `0`;
- custom device interface state: `Enabled`.

## 4. Launch the portable simulator

Run the simulator as a normal user, not as Administrator:

```powershell
cd "$env:USERPROFILE\Downloads\CodexMicroSimulator-v1.0.0-win-x64-portable-compatible"
.\CodexMicroSimulator.exe
```

The application is single-instance. It also starts a hidden `--micro-broker` child process that exclusively owns the driver handle and coordinates HID input and lighting output between the simulator and other local clients.

## 5. Understand the three status lights

From top to bottom:

| Light | Meaning | Normal or warning state |
| --- | --- | --- |
| First | Codex compatibility | Blue for a reviewed build; yellow for an unlisted newer build that is allowed to connect; red and blocked when a known build has a hash mismatch |
| Second | Virtual HID / Broker | Neutral while connected; yellow while disconnected; red after a runtime failure |
| Third | Most recent event | Neutral when ready; blue after delivery; yellow when the outcome is unknown; red after a send failure |

An unknown Codex version is indicated only by the first light turning yellow. The normal connection label does not show an additional warning, but hovering over the yellow light still displays the reason.

## 6. Reconnect and troubleshoot

Right-click the black settings knob in the lower-left corner to rerun the compatibility check and reconnect HID/Broker.

If the third light turns red with the message `The virtual HID path is not ready yet`:

1. Do not press another action key immediately; doing so may overwrite the first and most useful error.
2. Hover over the first and second lights to read the compatibility and driver states.
3. Right-click the black knob to reconnect.
4. Inspect `driver-install.log`, the PnP device state, and the device-interface state.
5. Confirm that no older simulator process still owns the same single-instance lock.

The original v1.0.0 build hard-blocked newer Codex builds that were absent from the manifest. For example, Codex `26.715.4045.0` displayed:

```text
This Codex build has no reviewed Micro compatibility manifest.
```

The compatible build uses a three-state policy instead: exact reviewed match, unknown version allowed with a yellow warning, and known mismatch blocked in red. It does not disable hash verification.

## 7. Recorded acceptance result

- Codex: `26.715.4045.0`;
- UMDF2/VHF driver: `1.0.0.5`;
- device state: `OK`, PnP error code `0`;
- custom interface: `Enabled`;
- automated tests: 5 protocol, 11 Broker, and 47 desktop tests; all 63 passed;
- live run: the main process and `--micro-broker` child remained stable;
- window status: `CodexMicroVhfUm / Broker connected`;
- Codex Agent lighting state synchronized to all six simulator slots through the driver output path.
