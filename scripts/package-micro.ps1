[CmdletBinding()]
param(
    [string]$Version = "0.2.1",
    [string]$Runtime = "win-x64",
    [double]$MaximumPackageMiB = 15
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))
$artifactRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $repoRoot ".artifacts\micro-release\$Version"))
$publishRoot = Join-Path $artifactRoot "publish"
$packageName = "CodexMicro-Keypad-$Version-$Runtime"
$packageRoot = Join-Path $artifactRoot $packageName
$distRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $repoRoot "dist"))
$zipPath = Join-Path $distRoot "$packageName.zip"
$checksumPath = "$zipPath.sha256"

function Assert-WorkspaceChild([string]$Path) {
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
        $artifactRoot,
        $distRoot,
        $zipPath,
        $checksumPath)) {
    Assert-WorkspaceChild $path
}

if ($MaximumPackageMiB -le 0) {
    throw "MaximumPackageMiB must be greater than zero."
}

if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null

$project = Join-Path $repoRoot `
    "virtual-micro\src\CodexMicro.DesktopHost\CodexMicro.DesktopHost.csproj"
& dotnet publish $project `
    -c Release `
    -r $Runtime `
    --self-contained false `
    --output $publishRoot `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "WPF keypad publish failed with exit code $LASTEXITCODE"
}

$executable = Join-Path $publishRoot "CodexMicro.exe"
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "WPF keypad publish did not produce CodexMicro.exe."
}

Copy-Item -LiteralPath $executable -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") `
    -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repoRoot "virtual-micro\README.md") `
    -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repoRoot "virtual-micro\README.zh-CN.md") `
    -Destination $packageRoot

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
if (Test-Path -LiteralPath $checksumPath) {
    Remove-Item -LiteralPath $checksumPath -Force
}

Compress-Archive `
    -LiteralPath $packageRoot `
    -DestinationPath $zipPath `
    -CompressionLevel Optimal

$maximumBytes = [long]($MaximumPackageMiB * 1MB)
$packageBytes = (Get-Item -LiteralPath $zipPath).Length
if ($packageBytes -gt $maximumBytes) {
    throw ("Package is {0:N2} MiB, above the {1:N2} MiB limit." -f `
        ($packageBytes / 1MB), $MaximumPackageMiB)
}

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256)
$checksumLine = "{0} *{1}" -f `
    $hash.Hash.ToLowerInvariant(), `
    [System.IO.Path]::GetFileName($zipPath)
Set-Content `
    -LiteralPath $checksumPath `
    -Value $checksumLine `
    -Encoding ascii

Write-Host ("Executable: {0:N2} MiB" -f `
    ((Get-Item -LiteralPath $executable).Length / 1MB))
Write-Host ("Package: {0} ({1:N2} MiB / {2:N2} MiB limit)" -f `
    $zipPath, ($packageBytes / 1MB), $MaximumPackageMiB)
Write-Host "SHA256: $($hash.Hash.ToLowerInvariant())"
