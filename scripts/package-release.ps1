[CmdletBinding()]
param(
    [string]$Version = "1.2.1",
    [string]$Runtime = "win-x64",
    [switch]$Compact,
    [double]$MaximumCompactPackageMiB = 30
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))
$packageSuffix = if ($Compact) { "-compact" } else { "" }
$packageName = "AgentController-$Version-$Runtime$packageSuffix"
$artifactRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $repoRoot ".artifacts\release\$Version\$packageName"))
$dotnetArtifactsRoot = Join-Path $artifactRoot "dotnet"
$publishRoot = Join-Path $artifactRoot "publish"
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

if ($Compact -and $MaximumCompactPackageMiB -le 0) {
    throw "MaximumCompactPackageMiB must be greater than zero."
}

if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null

$project = Join-Path $repoRoot "app\AgentController.csproj"
$selfContained = if ($Compact) { "false" } else { "true" }
$publishArguments = @(
    "publish", $project,
    "-c", "Release",
    "-r", $Runtime,
    "--artifacts-path", $dotnetArtifactsRoot,
    "--self-contained", $selfContained,
    "--output", $publishRoot,
    "-p:PublishSingleFile=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false")
if (-not $Compact) {
    $publishArguments += @(
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true")
}
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Get-ChildItem -LiteralPath $publishRoot | Copy-Item `
    -Destination $packageRoot `
    -Recurse `
    -Force

$docsRoot = Join-Path $packageRoot "DOCS"
New-Item -ItemType Directory -Path $docsRoot -Force | Out-Null
$docsImageRoot = Join-Path $docsRoot "public\images"
New-Item -ItemType Directory -Path $docsImageRoot -Force | Out-Null
$docsPublicRoot = Join-Path $docsRoot "public\docs"
New-Item -ItemType Directory -Path $docsPublicRoot -Force | Out-Null
$docsDesignRoot = Join-Path $docsRoot "docs"
New-Item -ItemType Directory -Path $docsDesignRoot -Force | Out-Null
$docsVirtualMicroRoot = Join-Path $docsRoot "virtual-micro"
New-Item -ItemType Directory -Path $docsVirtualMicroRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") `
    -Destination $docsRoot
Copy-Item -LiteralPath (Join-Path $repoRoot "README.zh-CN.md") `
    -Destination $docsRoot
Copy-Item -LiteralPath (
    Join-Path $repoRoot "public\images\agent-controller-gamepad-guide-en.png") `
    -Destination $docsImageRoot
Copy-Item -LiteralPath (
    Join-Path $repoRoot "public\images\agent-controller-gamepad-guide-zh-CN-v2.png") `
    -Destination $docsImageRoot
Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") `
    -Destination $packageRoot
Copy-Item -LiteralPath (
    Join-Path $repoRoot "public\docs\controller-operations.md") `
    -Destination $docsPublicRoot
Copy-Item -LiteralPath (
    Join-Path $repoRoot "public\docs\release-v$Version.md") `
    -Destination $docsPublicRoot
Copy-Item -LiteralPath (
    Join-Path $repoRoot "public\docs\architecture-and-input-flow.md") `
    -Destination $docsPublicRoot
Copy-Item -LiteralPath (
    Join-Path $repoRoot "public\docs\codex-micro-command-reference.md") `
    -Destination $docsPublicRoot
Copy-Item -LiteralPath (
    Join-Path $repoRoot "docs\CodexMicroSimulator-installation.md") `
    -Destination $docsDesignRoot
$installTutorials = @(
    Get-ChildItem `
        -LiteralPath (Join-Path $repoRoot "docs") `
        -Filter "CodexMicroSimulator-*.zh-CN.md" `
        -File)
if ($installTutorials.Count -ne 1) {
    throw "Expected exactly one Codex Micro installation tutorial, found $($installTutorials.Count)."
}
Copy-Item -LiteralPath $installTutorials[0].FullName `
    -Destination $docsDesignRoot
Copy-Item -LiteralPath (
    Join-Path $repoRoot "virtual-micro\UNSIGNED-DRIVER.md") `
    -Destination $docsVirtualMicroRoot
Copy-Item -LiteralPath (
    Join-Path $repoRoot "virtual-micro\UNSIGNED-DRIVER.zh-CN.md") `
    -Destination $docsVirtualMicroRoot

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

$packageBytes = (Get-Item -LiteralPath $zipPath).Length
if ($Compact) {
    $maximumBytes = [long]($MaximumCompactPackageMiB * 1MB)
    if ($packageBytes -gt $maximumBytes) {
        throw ("Compact package is {0:N2} MiB, above the {1:N2} MiB limit." -f `
            ($packageBytes / 1MB), $MaximumCompactPackageMiB)
    }
}

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256)
$checksumLine = "{0} *{1}" -f `
    $hash.Hash.ToLowerInvariant(), `
    [System.IO.Path]::GetFileName($zipPath)
Set-Content `
    -LiteralPath $checksumPath `
    -Value $checksumLine `
    -Encoding ascii

if ($Compact) {
    Write-Host ("Compact package: {0} ({1:N2} MiB / {2:N2} MiB limit)" -f `
        $zipPath, ($packageBytes / 1MB), $MaximumCompactPackageMiB)
}
else {
    Write-Host ("Self-contained package: {0} ({1:N2} MiB)" -f `
        $zipPath, ($packageBytes / 1MB))
}
Write-Host "SHA256: $($hash.Hash.ToLowerInvariant())"
