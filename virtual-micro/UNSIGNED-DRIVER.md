# Unsigned Codex Micro driver package

[简体中文](./UNSIGNED-DRIVER.zh-CN.md)

The Release asset named
`CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER.zip` is a developer artifact,
not a production installer. It contains prebuilt x64 UMDF2/VHF binaries so that
reviewers can audit and re-sign the package without compiling C/C++.

No certificate or private key is included. The DLL, catalog, and native helper
are all intentionally unsigned in the downloaded archive. The UMDF DLL and
native helper statically link their C/C++ runtimes, so the developer package
does not require a separate Visual C++ Redistributable.

## What is required

To locally re-sign and install the prebuilt package:

- Windows 10/11 x64 and an elevated PowerShell;
- SignTool from Windows SDK `10.0.26100.0`;
- Inf2Cat from pinned WDK NuGet package `Microsoft.Windows.WDK.x64`
  `10.0.26100.6584`;
- Visual Studio/Build Tools MSBuild only to restore the WDK NuGet package when
  it is not already cached. No C/C++ compilation is required for this route.

To rebuild the binaries instead, install Visual Studio/Build Tools 2022 with
Desktop development with C++, MSVC v143 x64/x86 tools, x64/x86
Spectre-mitigated libraries, Windows SDK `10.0.26100.0`, and MSBuild. The .NET
9 SDK is needed for the simulator app and tests, not for the UMDF2 driver alone.

## Restore signing tools without compiling

From the extracted package directory, locate MSBuild and restore the pinned WDK
NuGet package:

```powershell
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
  -find 'MSBuild\**\Bin\amd64\MSBuild.exe' | Select-Object -First 1

if (-not $msbuild) { throw 'Visual Studio MSBuild was not found.' }

& $msbuild '.\driver\CodexMicroVhfUm\CodexMicroVhfUm.vcxproj' `
  /restore /t:Restore /p:Configuration=Release /p:Platform=x64
```

## Easiest local secondary-signing path

Open an elevated PowerShell in the extracted `virtual-micro` directory and run:

```powershell
.\Install-CodexMicroDriver.ps1
```

The script creates a non-exportable local test code-signing certificate named
`CN=Codex Micro Simulator Driver`, trusts its public certificate on that
computer, embeds a signature in `CodexMicroVhfUm.dll`, deletes and regenerates
the catalog with Inf2Cat, signs the new catalog, installs
`Root\CodexMicroHidUm`, and verifies device health.

The catalog must be regenerated after signing the DLL because its stored DLL
hash has changed. Reversing that order produces an invalid driver package.

## Use an existing development certificate

The equivalent manual order below assumes the certificate and private key are
already in `Cert:\LocalMachine\My`. Replace `<THUMBPRINT>` and, if necessary,
the tool paths. Omit `/sm` when the certificate is in the current-user store.

```powershell
$package = (Resolve-Path '.\driver\CodexMicroVhfUm\x64\Release').Path
$signTool = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe'
$inf2Cat = Join-Path $env:USERPROFILE `
  '.nuget\packages\microsoft.windows.wdk.x64\10.0.26100.6584\c\bin\10.0.26100.0\x86\Inf2Cat.exe'
$thumbprint = '<THUMBPRINT>'

& $signTool sign /v /fd SHA256 /sha1 $thumbprint /sm `
  "$package\CodexMicroVhfUm.dll"

Remove-Item -LiteralPath "$package\CodexMicroVhfUm.cat" -Force
& $inf2Cat "/driver:$package" /os:10_X64

& $signTool sign /v /fd SHA256 /sha1 $thumbprint /sm `
  "$package\CodexMicroVhfUm.cat"

& $signTool verify /v /pa "$package\CodexMicroVhfUm.cat"
& $signTool verify /v /pa /c "$package\CodexMicroVhfUm.cat" `
  "$package\CodexMicroVhfUm.dll"
```

Trusting a locally issued certificate on the target machine is a separate
administrative action. A local or enterprise development signature is not a
substitute for Microsoft's production driver-signing process. Do not share a
private key or distribute the locally generated test certificate as a public
release identity.
