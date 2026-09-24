[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Version,
    [string]$TargetPath,
    [switch]$StopRunning,
    [switch]$Launch
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repoRoot `
    'virtual-micro\src\CodexMicro.DesktopHost\CodexMicro.DesktopHost.csproj'
$packageScript = Join-Path $PSScriptRoot 'package-micro.ps1'
$desktopRoot = [IO.Path]::GetFullPath(
    [Environment]::GetFolderPath('Desktop'))

if ([string]::IsNullOrWhiteSpace($TargetPath)) {
    $TargetPath = Join-Path $desktopRoot 'CodexMicro.exe'
}
$TargetPath = [IO.Path]::GetFullPath($TargetPath)

if (-not [IO.Path]::GetDirectoryName($TargetPath).Equals(
        $desktopRoot,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "TargetPath must point directly inside the current user's Desktop: $TargetPath"
}
if ([IO.Path]::GetExtension($TargetPath) -cne '.exe') {
    throw "TargetPath must be an executable: $TargetPath"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    if (Test-Path -LiteralPath $TargetPath -PathType Leaf) {
        $installedVersion = (Get-Item -LiteralPath $TargetPath).VersionInfo.ProductVersion
        if ($installedVersion -match '^\d+\.\d+\.\d+(?:[-+].*)?$') {
            $Version = $installedVersion
        }
    }
    if ([string]::IsNullOrWhiteSpace($Version)) {
        [xml]$project = Get-Content -LiteralPath $projectPath -Raw
        $Version = @($project.Project.PropertyGroup |
            ForEach-Object { [string]$_.Version } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) })[0]
    }
}
if ($Version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
    throw "Version must be a semantic version: $Version"
}

if (-not $PSCmdlet.ShouldProcess(
        $TargetPath,
        "Package Codex Micro Monitor $Version and replace the desktop executable")) {
    return
}

& $packageScript -Version $Version -Preset monitor
if ($LASTEXITCODE -ne 0) {
    throw "Monitor packaging failed with exit code $LASTEXITCODE."
}

$sourcePath = Join-Path $repoRoot `
    ".artifacts\micro-release\$Version\publish\CodexMicro.exe"
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "Packaged executable is missing: $sourcePath"
}

$targetProcesses = @(Get-CimInstance Win32_Process -Filter "Name='CodexMicro.exe'" |
    Where-Object {
        -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
        [IO.Path]::GetFullPath($_.ExecutablePath).Equals(
            $TargetPath,
            [StringComparison]::OrdinalIgnoreCase)
    })
if ($targetProcesses.Count -gt 0 -and -not $StopRunning) {
    throw "The desktop CodexMicro.exe is running. Close it or pass -StopRunning."
}
foreach ($process in $targetProcesses) {
    Stop-Process -Id $process.ProcessId -Force
    Wait-Process -Id $process.ProcessId -Timeout 10 -ErrorAction SilentlyContinue
}

$backupPath = $null
if (Test-Path -LiteralPath $TargetPath -PathType Leaf) {
    $backupRoot = Join-Path $repoRoot (
        '.artifacts\local-deploy\codex-micro\backups\' +
        (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' +
        [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $backupPath = Join-Path $backupRoot 'CodexMicro.exe'
    Copy-Item -LiteralPath $TargetPath -Destination $backupPath
}

$stagingPath = "$TargetPath.$PID.new"
try {
    Copy-Item -LiteralPath $sourcePath -Destination $stagingPath -Force
    $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
    $stagingHash = (Get-FileHash -LiteralPath $stagingPath -Algorithm SHA256).Hash
    if ($sourceHash -cne $stagingHash) {
        throw 'The staged desktop executable failed SHA-256 verification.'
    }
    [IO.File]::Move($stagingPath, $TargetPath, $true)
}
finally {
    if (Test-Path -LiteralPath $stagingPath -PathType Leaf) {
        Remove-Item -LiteralPath $stagingPath -Force
    }
}

$deployedHash = (Get-FileHash -LiteralPath $TargetPath -Algorithm SHA256).Hash
if ($deployedHash -cne $sourceHash) {
    throw 'The deployed desktop executable failed SHA-256 verification.'
}

Write-Host "Desktop application: $TargetPath"
Write-Host "Version: $Version"
Write-Host "SHA256: $($deployedHash.ToLowerInvariant())"
if ($null -ne $backupPath) {
    Write-Host "Previous executable: $backupPath"
}

if ($Launch) {
    Start-Process -FilePath $TargetPath
}
