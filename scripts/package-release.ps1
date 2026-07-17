[CmdletBinding()]
param(
    [string]$Version = "0.7.0",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))
$artifactRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $repoRoot ".artifacts\release\$Version"))
$publishRoot = Join-Path $artifactRoot "publish"
$packageName = "AgentController-$Version-$Runtime"
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

Assert-WorkspaceChild $artifactRoot
Assert-WorkspaceChild $distRoot
Assert-WorkspaceChild $zipPath
Assert-WorkspaceChild $checksumPath

if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null

$project = Join-Path $repoRoot "app\AgentController.csproj"
& dotnet publish $project `
    -c Release `
    -r $Runtime `
    --self-contained true `
    --output $publishRoot `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Get-ChildItem -LiteralPath $publishRoot | Copy-Item `
    -Destination $packageRoot `
    -Recurse `
    -Force

$docsRoot = Join-Path $packageRoot "DOCS"
New-Item -ItemType Directory -Path $docsRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") `
    -Destination $docsRoot
Copy-Item -LiteralPath (Join-Path $repoRoot "README.zh-CN.md") `
    -Destination $docsRoot
Copy-Item -LiteralPath (
    Join-Path $repoRoot "public\docs\controller-command-reference-v0.7.md") `
    -Destination $docsRoot
Copy-Item -LiteralPath (
    Join-Path $repoRoot "public\docs\release-v0.7.md") `
    -Destination $docsRoot
Copy-Item -LiteralPath (
    Join-Path $repoRoot "public\docs\codex-micro-virtual-hid-bridge-plan.md") `
    -Destination $docsRoot
Copy-Item -LiteralPath (
    Join-Path $repoRoot "docs\codex-26.707.12708-vhf-status-input.zh-CN.md") `
    -Destination $docsRoot

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

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256)
$checksumLine = "{0} *{1}" -f `
    $hash.Hash.ToLowerInvariant(), `
    [System.IO.Path]::GetFileName($zipPath)
Set-Content `
    -LiteralPath $checksumPath `
    -Value $checksumLine `
    -Encoding ascii

Write-Host "Package: $zipPath"
Write-Host "SHA256: $($hash.Hash.ToLowerInvariant())"
