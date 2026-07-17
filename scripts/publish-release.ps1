[CmdletBinding()]
param(
    [string]$Version = "0.7.0-hotfix",
    [string]$Runtime = "win-x64",
    [string]$Repository = "",
    [string]$Tag = "",
    [string]$NotesFile = "public\docs\release-v0.7.md",
    [switch]$SkipBuild,
    [switch]$Draft,
    [switch]$Prerelease
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))
$releaseVersion = $Version.TrimStart("v")
if ([string]::IsNullOrWhiteSpace($Tag)) {
    $Tag = "v$releaseVersion"
}

function Invoke-Checked(
    [string]$Command,
    [string[]]$Arguments
) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE."
    }
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI is required. Install it with: winget install --id GitHub.cli"
}

Push-Location $repoRoot
try {
    Invoke-Checked "gh" @("auth", "status")

    if ([string]::IsNullOrWhiteSpace($Repository)) {
        $repositoryOutput = & gh repo view `
            --json nameWithOwner `
            --jq ".nameWithOwner"
        if ($LASTEXITCODE -ne 0 -or
            [string]::IsNullOrWhiteSpace($repositoryOutput)) {
            throw "Could not determine the GitHub repository from the current checkout."
        }
        $Repository = ($repositoryOutput | Select-Object -First 1).Trim()
    }

    Invoke-Checked "git" @(
        "ls-remote",
        "--exit-code",
        "--tags",
        "origin",
        "refs/tags/$Tag")

    if (-not $SkipBuild) {
        & (Join-Path $PSScriptRoot "package-release.ps1") `
            -Version $releaseVersion `
            -Runtime $Runtime
        if ($LASTEXITCODE -ne 0) {
            throw "Release packaging failed with exit code $LASTEXITCODE."
        }
    }

    $packageName = "AgentController-$releaseVersion-$Runtime"
    $zipPath = Join-Path $repoRoot "dist\$packageName.zip"
    $checksumPath = "$zipPath.sha256"
    $notesPath = [System.IO.Path]::GetFullPath(
        (Join-Path $repoRoot $NotesFile))

    foreach ($path in @($zipPath, $checksumPath, $notesPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required release file is missing: $path"
        }
    }

    $checksumLine = (
        Get-Content -LiteralPath $checksumPath -Encoding ascii |
            Select-Object -First 1)
    if ($checksumLine -notmatch `
        "^(?<hash>[0-9a-fA-F]{64})\s+\*?(?<file>.+)$") {
        throw "Invalid SHA-256 file format: $checksumPath"
    }

    $actualHash = (
        Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    $declaredHash = $Matches.hash.ToLowerInvariant()
    $declaredFile = $Matches.file.Trim()
    if ($actualHash -ne $declaredHash -or
        $declaredFile -ne [System.IO.Path]::GetFileName($zipPath)) {
        throw "SHA-256 verification failed for $zipPath"
    }

    $title = "Agent Controller v$releaseVersion"
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "SilentlyContinue"
        & gh release view $Tag --repo $Repository *> $null
        $releaseExists = $LASTEXITCODE -eq 0
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($releaseExists) {
        $editArguments = @(
            "release", "edit", $Tag,
            "--repo", $Repository,
            "--title", $title,
            "--notes-file", $notesPath)
        if ($Draft) {
            $editArguments += "--draft"
        }
        if ($Prerelease) {
            $editArguments += "--prerelease"
        }
        Invoke-Checked "gh" $editArguments
        Invoke-Checked "gh" @(
            "release", "upload", $Tag,
            $zipPath, $checksumPath,
            "--repo", $Repository,
            "--clobber")
    }
    else {
        $createArguments = @(
            "release", "create", $Tag,
            $zipPath, $checksumPath,
            "--repo", $Repository,
            "--title", $title,
            "--notes-file", $notesPath,
            "--verify-tag")
        if ($Draft) {
            $createArguments += "--draft"
        }
        if ($Prerelease) {
            $createArguments += "--prerelease"
        }
        Invoke-Checked "gh" $createArguments
    }

    Invoke-Checked "gh" @(
        "release", "view", $Tag,
        "--repo", $Repository,
        "--json", "url,name,tagName,isDraft,isPrerelease")
}
finally {
    Pop-Location
}
