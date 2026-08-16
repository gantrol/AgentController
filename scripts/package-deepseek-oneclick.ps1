[CmdletBinding()]
param(
    [string]$Version = "0.2.4",
    [string]$Runtime = "win-x64",
    [string]$BundledWslPayload,
    [double]$MaximumPackageMiB = 1024,
    [switch]$NoDotNet
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($BundledWslPayload)) {
    $BundledWslPayload = Join-Path $repoRoot `
        ".artifacts\deepseek-full\$Version\deepseek-runtime.wsl"
}
$BundledWslPayload = [System.IO.Path]::GetFullPath($BundledWslPayload)
$distRoot = Join-Path $repoRoot "dist"
$packageName = if ($NoDotNet) {
    "Deepseek-Harness-Keypad-Full-NoDotNet-v$Version-$Runtime"
}
else {
    "Deepseek-Harness-Keypad-Full-v$Version-$Runtime"
}
$payloadZip = Join-Path $distRoot "$packageName.zip"
$payloadChecksum = "$payloadZip.sha256"
$outputName = if ($NoDotNet) {
    "Deepseek-Harness-Keypad-Full-v$Version-oneclick-no-dotnet.exe"
}
else {
    "Deepseek-Harness-Keypad-Full-v$Version-oneclick.exe"
}
$outputPath = Join-Path $distRoot $outputName
$checksumPath = "$outputPath.sha256"
$artifactVersion = if ($NoDotNet) { "$Version-no-dotnet" } else { $Version }
$artifactRoot = Join-Path $repoRoot `
    ".artifacts\oneclick-release\$artifactVersion"
$publishRoot = Join-Path $artifactRoot "bootstrap"
$project = Join-Path $repoRoot `
    "virtual-micro\src\DeepSeekKeypad.OneClick\DeepSeekKeypad.OneClick.csproj"

function Assert-RepositoryChild([string]$Path) {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $prefix = $repoRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith(
            $prefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the repository: $fullPath"
    }
}

foreach ($path in @(
        $distRoot,
        $payloadZip,
        $payloadChecksum,
        $outputPath,
        $checksumPath,
        $artifactRoot,
        $publishRoot)) {
    Assert-RepositoryChild $path
}
if (-not (Test-Path -LiteralPath $BundledWslPayload -PathType Leaf)) {
    throw "Bundled WSL payload is missing: $BundledWslPayload"
}
if ($MaximumPackageMiB -le 0) {
    throw "MaximumPackageMiB must be greater than zero."
}
if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
foreach ($path in @($outputPath, $checksumPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

$packageArguments = @{
    Version = $Version
    Runtime = $Runtime
    Preset = "deepseek-full"
    BundledWslPayload = $BundledWslPayload
    MaximumPackageMiB = $MaximumPackageMiB
}
if ($NoDotNet) {
    $packageArguments.FrameworkDependent = $true
}
& (Join-Path $PSScriptRoot "package-micro.ps1") @packageArguments
if ($LASTEXITCODE -ne 0) {
    throw "Full keypad payload packaging failed with exit code $LASTEXITCODE."
}

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
$bootstrapSelfContained = if ($NoDotNet) { "false" } else { "true" }
& dotnet publish $project `
    -c Release `
    -r $Runtime `
    --self-contained $bootstrapSelfContained `
    --output $publishRoot `
    -p:Version=$Version `
    -p:InformationalVersion=$Version `
    -p:IncludeSourceRevisionInInformationalVersion=false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=$bootstrapSelfContained `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "OneClick bootstrap publish failed with exit code $LASTEXITCODE."
}
$bootstrap = Join-Path $publishRoot "Deepseek-Harness-Keypad-OneClick.exe"
if (-not (Test-Path -LiteralPath $bootstrap -PathType Leaf)) {
    throw "OneClick bootstrap executable is missing."
}
if (-not (Test-Path -LiteralPath $payloadZip -PathType Leaf)) {
    throw "OneClick payload ZIP is missing."
}

Copy-Item -LiteralPath $bootstrap -Destination $outputPath
$payloadHash = Get-FileHash -LiteralPath $payloadZip -Algorithm SHA256
$payloadHashBytes = [Convert]::FromHexString($payloadHash.Hash)
$outputStream = [System.IO.File]::Open(
    $outputPath,
    [System.IO.FileMode]::Append,
    [System.IO.FileAccess]::Write,
    [System.IO.FileShare]::None)
try {
    $payloadOffset = $outputStream.Position
    $payloadStream = [System.IO.File]::OpenRead($payloadZip)
    try {
        $payloadLength = $payloadStream.Length
        $payloadStream.CopyTo($outputStream, 1024 * 1024)
    }
    finally {
        $payloadStream.Dispose()
    }

    $footer = [byte[]]::new(64)
    $magic = [System.Text.Encoding]::ASCII.GetBytes("DSHKP_ONECLICK_1")
    [Array]::Copy($magic, 0, $footer, 0, $magic.Length)
    [Array]::Copy(
        [BitConverter]::GetBytes([long]$payloadOffset),
        0,
        $footer,
        16,
        8)
    [Array]::Copy(
        [BitConverter]::GetBytes([long]$payloadLength),
        0,
        $footer,
        24,
        8)
    [Array]::Copy($payloadHashBytes, 0, $footer, 32, 32)
    $outputStream.Write($footer, 0, $footer.Length)
}
finally {
    $outputStream.Dispose()
}

$maximumBytes = [long]($MaximumPackageMiB * 1MB)
$outputBytes = (Get-Item -LiteralPath $outputPath).Length
if ($outputBytes -gt $maximumBytes) {
    throw ("OneClick package is {0:N2} MiB, above the {1:N2} MiB limit." -f `
        ($outputBytes / 1MB), $MaximumPackageMiB)
}
$hash = Get-FileHash -LiteralPath $outputPath -Algorithm SHA256
$checksumLine = "{0} *{1}" -f `
    $hash.Hash.ToLowerInvariant(), `
    [System.IO.Path]::GetFileName($outputPath)
Set-Content `
    -LiteralPath $checksumPath `
    -Value $checksumLine `
    -Encoding ascii

# The Full ZIP is an implementation detail of the single-file installer, not
# a release asset. Keep the assembled package root under .artifacts for QA.
Remove-Item -LiteralPath $payloadZip -Force
Remove-Item -LiteralPath $payloadChecksum -Force

Write-Host ("OneClick: {0} ({1:N2} MiB / {2:N2} MiB limit)" -f `
    $outputPath, ($outputBytes / 1MB), $MaximumPackageMiB)
Write-Host "SHA256: $($hash.Hash.ToLowerInvariant())"
