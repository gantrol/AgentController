[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$package = Join-Path $root 'driver\CodexMicroVhfUm\x64\Release'
$inf = Join-Path $package 'CodexMicroVhfUm.inf'
$driverBinary = Join-Path $package 'CodexMicroVhfUm.dll'
$catalog = Join-Path $package 'CodexMicroVhfUm.cat'
$installer = Join-Path $root 'tools\CodexMicro.DriverInstaller.Native\bin\Release\CodexMicro.DriverInstaller.Native.exe'
$wdkBin = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0'
$signTool = Join-Path $wdkBin 'x64\signtool.exe'
$inf2Cat = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.wdk.x64\10.0.26100.6584\c\bin\10.0.26100.0\x86\Inf2Cat.exe'
$pnputil = Join-Path $env:SystemRoot 'System32\pnputil.exe'
$subject = 'CN=Codex Micro Simulator Driver'
$vhfHardwareId = 'Root\CodexMicroHidUm'
$legacyHardwareIds = @('Root\CodexMicroVhfUm', 'Root\CodexMicroVhf')

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'Administrator permission is required. Opening the Windows UAC prompt...'
    $elevated = Start-Process `
        -FilePath 'powershell.exe' `
        -Verb RunAs `
        -Wait `
        -PassThru `
        -ArgumentList @(
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            "`"$($MyInvocation.MyCommand.Path)`"")
    exit $elevated.ExitCode
}

$transcriptPath = Join-Path $root 'driver-install.log'
Start-Transcript -Path $transcriptPath -Force | Out-Null
try {
    Write-Host '[1/4] Checking the local driver package...'
    foreach ($path in @($inf, $driverBinary, $installer, $signTool, $inf2Cat, $pnputil)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required file not found: $path"
        }
    }

    $cert = Get-ChildItem Cert:\LocalMachine\My |
        Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1

    if (-not $cert) {
        $cert = New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject $subject `
            -CertStoreLocation Cert:\LocalMachine\My `
            -KeyAlgorithm RSA `
            -KeyLength 3072 `
            -HashAlgorithm SHA256 `
            -KeyExportPolicy NonExportable `
            -NotAfter (Get-Date).AddYears(5)
    }

    $certificatePath = Join-Path $package 'CodexMicroSimulatorDriver.cer'
    Export-Certificate -Cert $cert -FilePath $certificatePath -Force | Out-Null
    Import-Certificate -FilePath $certificatePath -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
    Import-Certificate -FilePath $certificatePath -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null

    Write-Host '[2/4] Signing the driver package...'
    & $signTool sign /v /fd SHA256 /sha1 $cert.Thumbprint /sm $driverBinary
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed for $driverBinary"
    }

    if (Test-Path -LiteralPath $catalog -PathType Leaf) {
        Remove-Item -LiteralPath $catalog -Force
    }
    & $inf2Cat "/driver:$package" /os:10_X64
    if ($LASTEXITCODE -ne 0) {
        throw 'Inf2Cat failed.'
    }

    & $signTool sign /v /fd SHA256 /sha1 $cert.Thumbprint /sm $catalog
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed for $catalog"
    }

    Write-Host '[3/4] Installing or refreshing the virtual HID...'
    # Updating an active UMDF/VHF source in place leaves its HID descriptor
    # cached until a system reboot. Remove only this simulator root before
    # reinstalling so descriptor changes enumerate immediately.
    $existingVhfSources = @(Get-CimInstance Win32_PnPEntity |
        Where-Object { @($_.HardwareID) -contains $vhfHardwareId })
    foreach ($existingVhfSource in $existingVhfSources) {
        Write-Host "Refreshing simulator device $($existingVhfSource.PNPDeviceID)"
        & $pnputil /remove-device $existingVhfSource.PNPDeviceID /subtree
        # pnputil returns ERROR_SUCCESS_REBOOT_REQUIRED (3010) even though the
        # devnode is already gone. Continue with a fresh root enumeration; the
        # new instance does not require a system restart.
        if ($LASTEXITCODE -notin @(0, 3010)) {
            throw "Failed to refresh simulator device $($existingVhfSource.PNPDeviceID)."
        }
    }

    & $installer install $inf $vhfHardwareId
    if ($LASTEXITCODE -ne 0) {
        throw 'VHF PnP installation failed.'
    }

    $vhfSource = Get-CimInstance Win32_PnPEntity |
        Where-Object { @($_.HardwareID) -contains $vhfHardwareId } |
        Select-Object -First 1
    if (-not $vhfSource) {
        throw 'The VHF source device did not appear after installation.'
    }
    if ($vhfSource.ConfigManagerErrorCode -ne 0) {
        throw "The VHF source device reported ConfigManager error $($vhfSource.ConfigManagerErrorCode)."
    }

    Write-Host '[4/4] Verifying device health...'
    # Remove only obsolete simulator root devices after the user-mode VHF
    # source is confirmed healthy. HID children leave with the same subtree.
    $legacyDevices = @(Get-CimInstance Win32_PnPEntity |
        Where-Object {
            $deviceHardwareIds = @($_.HardwareID)
            @($legacyHardwareIds | Where-Object {
                $deviceHardwareIds -contains $_
            }).Count -gt 0
        })
    foreach ($device in $legacyDevices) {
        Write-Host "Removing legacy simulator device $($device.PNPDeviceID)"
        & $pnputil /remove-device $device.PNPDeviceID /subtree
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to remove legacy device $($device.PNPDeviceID)."
        }
    }

    $installedDriver = Get-CimInstance Win32_PnPSignedDriver |
        Where-Object { $_.DeviceID -eq $vhfSource.PNPDeviceID } |
        Select-Object -First 1
    $driverVersion = if ($installedDriver) {
        $installedDriver.DriverVersion
    }
    else {
        'unknown'
    }
    Write-Host "Ready: $($vhfSource.PNPDeviceID) (driver $driverVersion)"
    Write-Host "Log: $transcriptPath"
}
finally {
    Stop-Transcript | Out-Null
}
