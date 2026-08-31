[CmdletBinding()]
param(
    [string]$Version = "0.2.9",
    [string]$Runtime = "win-x64",
    [double]$MaximumPackageMiB = 15,
    [ValidateSet("standard", "deepseek", "deepseek-full")]
    [string]$Preset = "standard",
    [string]$BundledWslPayload,
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))
$presetId = $Preset.ToLowerInvariant()
$isDeepSeek = $presetId -in @("deepseek", "deepseek-full")
$isFull = $presetId -eq "deepseek-full"
$isSelfContained = $isFull -and -not $FrameworkDependent
$artifactVersion = if ($isFull -and $FrameworkDependent) {
    "$Version-full-no-dotnet"
} elseif ($isFull) {
    "$Version-full"
} else {
    $Version
}
$artifactRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $repoRoot ".artifacts\micro-release\$artifactVersion"))
$publishRoot = Join-Path $artifactRoot "publish"
$packageName = if ($isFull -and $FrameworkDependent) {
    "Deepseek-Harness-Keypad-Full-NoDotNet-v$Version-$Runtime"
} elseif ($isFull) {
    "Deepseek-Harness-Keypad-Full-v$Version-$Runtime"
} elseif ($isDeepSeek) {
    "Deepseek-Harness-Keypad-v$Version-$Runtime"
} else {
    "CodexMicro-Keypad-$Version-$Runtime"
}
$productName = if ($isDeepSeek) {
    "Deepseek Harness Keypad"
} else {
    "Codex Micro Keypad"
}
$packageRoot = Join-Path $artifactRoot $packageName
$distRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $repoRoot "dist"))
$zipPath = Join-Path $distRoot "$packageName.zip"
$checksumPath = "$zipPath.sha256"
$pluginPackageName = "Deepseek-Harness-Keypad-Bridge-v$Version"
$pluginPackageRoot = Join-Path $artifactRoot $pluginPackageName
$pluginZipPath = Join-Path $distRoot "$pluginPackageName.zip"
$pluginChecksumPath = "$pluginZipPath.sha256"

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
        $checksumPath,
        $pluginPackageRoot,
        $pluginZipPath,
        $pluginChecksumPath)) {
    Assert-WorkspaceChild $path
}

if ($MaximumPackageMiB -le 0) {
    throw "MaximumPackageMiB must be greater than zero."
}
if ($isFull) {
    if ([string]::IsNullOrWhiteSpace($BundledWslPayload)) {
        throw "BundledWslPayload is required for the deepseek-full preset."
    }
    $BundledWslPayload = [System.IO.Path]::GetFullPath($BundledWslPayload)
    if (-not (Test-Path -LiteralPath $BundledWslPayload -PathType Leaf)) {
        throw "Bundled WSL payload is missing: $BundledWslPayload"
    }
}

if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null

$project = Join-Path $repoRoot `
    "virtual-micro\src\CodexMicro.DesktopHost\CodexMicro.DesktopHost.csproj"
$selfContained = if ($isSelfContained) { "true" } else { "false" }
& dotnet publish $project `
    -c Release `
    -r $Runtime `
    --self-contained $selfContained `
    --output $publishRoot `
    -p:Version=$Version `
    -p:InformationalVersion=$Version `
    -p:IncludeSourceRevisionInInformationalVersion=false `
    "-p:Product=$productName" `
    "-p:AssemblyTitle=$productName" `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=$selfContained `
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
Copy-Item -LiteralPath (Join-Path $repoRoot `
    "virtual-micro\DEEPSEEK-WINDOWS-SETUP.zh-CN.md") `
    -Destination $packageRoot
$voiceSource = Join-Path $repoRoot "virtual-micro\voice"
$voiceTarget = Join-Path $packageRoot "voice"
if (-not (Test-Path -LiteralPath $voiceSource -PathType Container)) {
    throw "Keypad-owned voice runtime is missing: $voiceSource"
}
New-Item -ItemType Directory -Path $voiceTarget -Force | Out-Null
foreach ($voiceFile in @(
        "start-qwen3-asr-stream.ps1",
        "qwen3-asr-stream-server.py")) {
    $voicePath = Join-Path $voiceSource $voiceFile
    if (-not (Test-Path -LiteralPath $voicePath -PathType Leaf)) {
        throw "Keypad-owned voice file is missing: $voicePath"
    }
    Copy-Item -LiteralPath $voicePath -Destination $voiceTarget
}

if ($isDeepSeek) {
    $presetPath = Join-Path $repoRoot `
        "virtual-micro\distribution-presets\deepseek.json"
    if (-not (Test-Path -LiteralPath $presetPath -PathType Leaf)) {
        throw "DeepSeek distribution preset is missing: $presetPath"
    }

    $presetTarget = Join-Path $packageRoot "distribution-preset.json"
    if ($isFull) {
        $presetDocument = Get-Content -LiteralPath $presetPath -Raw |
            ConvertFrom-Json
        $presetDocument.firstRun.installBundledHarness = $true
        $presetDocument | ConvertTo-Json -Depth 10 |
            Set-Content -LiteralPath $presetTarget -Encoding utf8
    }
    else {
        Copy-Item -LiteralPath $presetPath -Destination $presetTarget
    }

    $pluginSource = Join-Path $repoRoot "micro-bridge\DeepSeekHarness"
    $pluginTarget = Join-Path $packageRoot "plugins\DeepSeekHarness"
    Push-Location $pluginSource
    try {
        & pnpm run build
        if ($LASTEXITCODE -ne 0) {
            throw "DeepSeek bridge build failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }

    New-Item -ItemType Directory -Path $pluginPackageRoot -Force | Out-Null
    foreach ($file in @(
            "package.json",
            "cordis.patch.yml",
            "README.md",
            "README.zh.md")) {
        Copy-Item -LiteralPath (Join-Path $pluginSource $file) `
            -Destination $pluginPackageRoot
    }
    $pluginLibTarget = Join-Path $pluginPackageRoot "lib"
    New-Item -ItemType Directory -Path $pluginLibTarget -Force | Out-Null
    foreach ($file in @("index.js", "client.js")) {
        Copy-Item -LiteralPath (Join-Path $pluginSource "lib\$file") `
            -Destination $pluginLibTarget
    }

    $pluginScriptsTarget = Join-Path $pluginPackageRoot "scripts"
    New-Item -ItemType Directory -Path $pluginScriptsTarget -Force | Out-Null
    foreach ($file in @(
            "configure-managed-settings.mjs",
            "install-dsh-wsl-runtime.sh",
            "install-windows.bat",
            "migrate-dsh-home-wsl.mjs",
            "prepare-managed-bridge.mjs",
            "runtime-versions.env",
            "start-dsh-wsl.sh",
            "uninstall-windows.bat")) {
        Copy-Item -LiteralPath (Join-Path $pluginSource "scripts\$file") `
            -Destination $pluginScriptsTarget
    }

    $prepareBridgeScript = Join-Path $pluginSource `
        "scripts\prepare-managed-bridge.mjs"
    & node $prepareBridgeScript $pluginPackageRoot $Version
    if ($LASTEXITCODE -ne 0) {
        throw "DeepSeek runtime bridge preparation failed with exit code $LASTEXITCODE"
    }

    New-Item -ItemType Directory -Path (Split-Path $pluginTarget) `
        -Force | Out-Null
    Copy-Item -LiteralPath $pluginPackageRoot `
        -Destination $pluginTarget `
        -Recurse
}

if ($isFull) {
    $payloadTarget = Join-Path $packageRoot "payload"
    New-Item -ItemType Directory -Path $payloadTarget -Force | Out-Null
    Copy-Item -LiteralPath $BundledWslPayload `
        -Destination (Join-Path $payloadTarget "deepseek-runtime.wsl")
    Copy-Item -LiteralPath (Join-Path $repoRoot `
        "scripts\Uninstall-DeepseekHarnessKeypad.ps1") `
        -Destination $packageRoot

    $marker = [ordered]@{
        ProductId = "deepseek-harness-keypad"
        Version = $Version
    }
    $marker | ConvertTo-Json |
        Set-Content `
            -LiteralPath (Join-Path $packageRoot `
                ".deepseek-harness-keypad.install.json") `
            -Encoding utf8

    $manifestFiles = @(
        Get-ChildItem -LiteralPath $packageRoot -File -Recurse -Force |
            Where-Object { $_.Name -ne "oneclick-manifest.json" } |
            Sort-Object FullName |
            ForEach-Object {
                [ordered]@{
                    Path = [System.IO.Path]::GetRelativePath(
                        $packageRoot,
                        $_.FullName).Replace('\', '/')
                    Size = $_.Length
                    Sha256 = (Get-FileHash `
                        -LiteralPath $_.FullName `
                        -Algorithm SHA256).Hash.ToLowerInvariant()
                }
            })
    $manifest = [ordered]@{
        SchemaVersion = 1
        ProductId = "deepseek-harness-keypad"
        Version = $Version
        Files = $manifestFiles
    }
    $manifest | ConvertTo-Json -Depth 6 |
        Set-Content `
            -LiteralPath (Join-Path $packageRoot "oneclick-manifest.json") `
            -Encoding utf8
}

$releasePaths = @($zipPath, $checksumPath)
if ($presetId -eq "deepseek") {
    $releasePaths += @($pluginZipPath, $pluginChecksumPath)
}
foreach ($releasePath in $releasePaths) {
    if (Test-Path -LiteralPath $releasePath) {
        Remove-Item -LiteralPath $releasePath -Force
    }
}

if ($isFull) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $packageRoot,
        $zipPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)
}
else {
    Compress-Archive `
        -LiteralPath $packageRoot `
        -DestinationPath $zipPath `
        -CompressionLevel Optimal
}

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

if ($presetId -eq "deepseek") {
    Compress-Archive `
        -LiteralPath $pluginPackageRoot `
        -DestinationPath $pluginZipPath `
        -CompressionLevel Optimal

    $pluginPackageBytes = (Get-Item -LiteralPath $pluginZipPath).Length
    if ($pluginPackageBytes -gt $maximumBytes) {
        throw ("Plugin package is {0:N2} MiB, above the {1:N2} MiB limit." -f `
            ($pluginPackageBytes / 1MB), $MaximumPackageMiB)
    }

    $pluginHash = Get-FileHash `
        -LiteralPath $pluginZipPath `
        -Algorithm SHA256
    $pluginChecksumLine = "{0} *{1}" -f `
        $pluginHash.Hash.ToLowerInvariant(), `
        [System.IO.Path]::GetFileName($pluginZipPath)
    Set-Content `
        -LiteralPath $pluginChecksumPath `
        -Value $pluginChecksumLine `
        -Encoding ascii
}

Write-Host ("Executable: {0:N2} MiB" -f `
    ((Get-Item -LiteralPath $executable).Length / 1MB))
Write-Host ("Package: {0} ({1:N2} MiB / {2:N2} MiB limit)" -f `
    $zipPath, ($packageBytes / 1MB), $MaximumPackageMiB)
Write-Host "SHA256: $($hash.Hash.ToLowerInvariant())"
if ($presetId -eq "deepseek") {
    Write-Host ("Plugin: {0} ({1:N2} MiB)" -f `
        $pluginZipPath, ($pluginPackageBytes / 1MB))
    Write-Host "Plugin SHA256: $($pluginHash.Hash.ToLowerInvariant())"
}
