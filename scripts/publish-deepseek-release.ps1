[CmdletBinding(SupportsShouldProcess, ConfirmImpact = "High")]
param(
    [string]$Version = "0.2.8",
    [string]$Repository = "gantrol/AgentController",
    [string]$Tag = "",
    [string]$NotesFile = "",
    [switch]$SkipBuild,
    [switch]$Draft,
    [switch]$Prerelease
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$releaseVersion = $Version.TrimStart("v")
if ($releaseVersion -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Version must be a semantic version such as 0.2.8."
}
if ([string]::IsNullOrWhiteSpace($Tag)) {
    $Tag = "codex-micro-v$releaseVersion"
}
if ([string]::IsNullOrWhiteSpace($NotesFile)) {
    $NotesFile = "public\docs\release-deepseek-keypad-v$releaseVersion.md"
}

$assetName = "DeepSeek-Keypad-Setup-$releaseVersion.exe"
$assetPath = Join-Path $repoRoot "dist\$assetName"
$checksumPath = "$assetPath.sha256"
$notesPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $NotesFile))
$payloadPath = Join-Path $repoRoot `
    ".artifacts\deepseek-full\$releaseVersion\deepseek-runtime.wsl"

function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE."
    }
}

function Assert-ReleaseAsset {
    foreach ($path in @($assetPath, $checksumPath, $notesPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required release file is missing: $path"
        }
    }

    $checksumLine = Get-Content -LiteralPath $checksumPath -Encoding ascii |
        Select-Object -First 1
    if ($checksumLine -notmatch `
        '^(?<hash>[0-9a-fA-F]{64})\s+\*?(?<file>.+)$') {
        throw "Invalid SHA-256 file format: $checksumPath"
    }
    $actualHash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).
        Hash.ToLowerInvariant()
    if ($actualHash -ne $Matches.hash.ToLowerInvariant() -or
        $Matches.file.Trim() -ne $assetName) {
        throw "SHA-256 verification failed for $assetPath"
    }
    return $actualHash
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI is required."
}

Push-Location $repoRoot
try {
    if (-not $SkipBuild) {
        & (Join-Path $PSScriptRoot "build-deepseek-full-payload.ps1") `
            -Version $releaseVersion `
            -OutputPath $payloadPath
        if ($LASTEXITCODE -ne 0) {
            throw "DeepSeek WSL payload build failed with exit code $LASTEXITCODE."
        }
        & (Join-Path $PSScriptRoot "package-deepseek-oneclick.ps1") `
            -Version $releaseVersion `
            -BundledWslPayload $payloadPath
        if ($LASTEXITCODE -ne 0) {
            throw "DeepSeek installer packaging failed with exit code $LASTEXITCODE."
        }
    }

    $assetHash = Assert-ReleaseAsset
    Write-Host "User release asset: $assetName"
    Write-Host "SHA256: $assetHash"
    Write-Host "No portable, no-.NET, payload, or Bridge variants will be uploaded."

    if (-not $PSCmdlet.ShouldProcess(
            "$Repository release $Tag",
            "Publish exactly one user-facing installer")) {
        return
    }

    Invoke-Checked "gh" @("auth", "status")
    Invoke-Checked "git" @(
        "ls-remote", "--exit-code", "--tags", "origin", "refs/tags/$Tag")

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "SilentlyContinue"
        & gh release view $Tag --repo $Repository *> $null
        $releaseExists = $LASTEXITCODE -eq 0
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    $title = "DeepSeek 键盘 $releaseVersion"
    if ($releaseExists) {
        $existingAssets = @(& gh release view $Tag `
            --repo $Repository `
            --json assets `
            --jq '.assets[].name')
        if ($LASTEXITCODE -ne 0) {
            throw "Could not inspect existing assets for $Tag."
        }
        $unexpectedAssets = @($existingAssets | Where-Object {
                -not [string]::Equals(
                    $_.Trim(),
                    $assetName,
                    [System.StringComparison]::Ordinal)
            })
        if ($unexpectedAssets.Count -ne 0) {
            throw ("Release $Tag already contains non-user assets: {0}. " +
                "Remove them explicitly before retrying." -f
                ($unexpectedAssets -join ', '))
        }
        $arguments = @(
            "release", "edit", $Tag,
            "--repo", $Repository,
            "--title", $title,
            "--notes-file", $notesPath)
        if ($Draft) { $arguments += "--draft" }
        if ($Prerelease) { $arguments += "--prerelease" }
        Invoke-Checked "gh" $arguments
        Invoke-Checked "gh" @(
            "release", "upload", $Tag, $assetPath,
            "--repo", $Repository, "--clobber")
    }
    else {
        $arguments = @(
            "release", "create", $Tag, $assetPath,
            "--repo", $Repository,
            "--title", $title,
            "--notes-file", $notesPath,
            "--verify-tag")
        if ($Draft) { $arguments += "--draft" }
        if ($Prerelease) { $arguments += "--prerelease" }
        Invoke-Checked "gh" $arguments
    }

    $publishedAssets = @(& gh release view $Tag `
        --repo $Repository `
        --json assets `
        --jq '.assets[].name')
    if ($LASTEXITCODE -ne 0 -or
        $publishedAssets.Count -ne 1 -or
        $publishedAssets[0].Trim() -ne $assetName) {
        throw "Release verification failed: expected exactly $assetName."
    }
}
finally {
    Pop-Location
}
