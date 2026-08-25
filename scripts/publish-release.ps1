[CmdletBinding()]
param(
    [string]$Version = "1.2.1",
    [string]$Runtime = "win-x64",
    [string]$Repository = "",
    [string]$Tag = "",
    [string]$NotesFile = "public\docs\release-v1.2.1.md",
    [switch]$IncludeCompact,
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
        if ($IncludeCompact) {
            & (Join-Path $PSScriptRoot "package-release.ps1") `
                -Version $releaseVersion `
                -Runtime $Runtime `
                -Compact
            if ($LASTEXITCODE -ne 0) {
                throw "Compact release packaging failed with exit code $LASTEXITCODE."
            }
        }
    }

    $packageName = "AgentController-$releaseVersion-$Runtime"
    $zipPath = Join-Path $repoRoot "dist\$packageName.zip"
    $checksumPath = "$zipPath.sha256"
    $notesPath = [System.IO.Path]::GetFullPath(
        (Join-Path $repoRoot $NotesFile))

    $releaseFiles = @($zipPath, $checksumPath)
    if ($IncludeCompact) {
        $compactPackageName = "$packageName-compact"
        $compactZipPath = Join-Path $repoRoot "dist\$compactPackageName.zip"
        $compactChecksumPath = "$compactZipPath.sha256"
        $releaseFiles += @($compactZipPath, $compactChecksumPath)
    }

    foreach ($path in @($releaseFiles + $notesPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required release file is missing: $path"
        }
    }

    for ($index = 0; $index -lt $releaseFiles.Count; $index += 2) {
        $currentZipPath = $releaseFiles[$index]
        $currentChecksumPath = $releaseFiles[$index + 1]
        $checksumLine = (
            Get-Content -LiteralPath $currentChecksumPath -Encoding ascii |
                Select-Object -First 1)
        if ($checksumLine -notmatch `
            "^(?<hash>[0-9a-fA-F]{64})\s+\*?(?<file>.+)$") {
            throw "Invalid SHA-256 file format: $currentChecksumPath"
        }

        $actualHash = (
            Get-FileHash -LiteralPath $currentZipPath -Algorithm SHA256
        ).Hash.ToLowerInvariant()
        $declaredHash = $Matches.hash.ToLowerInvariant()
        $declaredFile = $Matches.file.Trim()
        if ($actualHash -ne $declaredHash -or
            $declaredFile -ne [System.IO.Path]::GetFileName($currentZipPath)) {
            throw "SHA-256 verification failed for $currentZipPath"
        }
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
        $uploadArguments = @(
            "release", "upload", $Tag) +
            $releaseFiles +
            @(
                "--repo", $Repository,
                "--clobber")
        Invoke-Checked "gh" $uploadArguments
    }
    else {
        $createArguments = @(
            "release", "create", $Tag) +
            $releaseFiles +
            @(
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
