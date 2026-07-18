[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputPath = Join-Path $root 'driver-policy.log'
$bcdedit = Join-Path $env:SystemRoot 'System32\bcdedit.exe'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this diagnostic elevated.'
}

$lines = [System.Collections.Generic.List[string]]::new()
try {
    $lines.Add("SecureBoot=$([bool](Confirm-SecureBootUEFI))")
}
catch {
    $lines.Add("SecureBoot=Unavailable ($($_.Exception.Message))")
}

try {
    $deviceGuard = Get-CimInstance `
        -Namespace 'root\Microsoft\Windows\DeviceGuard' `
        -ClassName Win32_DeviceGuard
    $lines.Add("VirtualizationBasedSecurityStatus=$($deviceGuard.VirtualizationBasedSecurityStatus)")
    $lines.Add("CodeIntegrityServicesRunning=$(@($deviceGuard.SecurityServicesRunning) -join ',')")
}
catch {
    $lines.Add("DeviceGuard=Unavailable ($($_.Exception.Message))")
}

$lines.Add('BCD-BEGIN')
$lines.AddRange([string[]](& $bcdedit /enum all 2>&1))
$lines.Add('BCD-END')
$lines | Set-Content -LiteralPath $outputPath -Encoding UTF8
Write-Host "Driver policy written to $outputPath"
