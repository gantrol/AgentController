[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Version,
    [ValidateSet('win-x64')]
    [string]$Runtime = "win-x64",
    [string]$Repository = "",
    [string]$Tag = "",
    [string]$NotesFile = "",
    [switch]$IncludeCompact,
    [switch]$SkipBuild,
    [switch]$Draft,
    [switch]$Prerelease,
    [switch]$ReplaceAssets
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))
. (Join-Path $PSScriptRoot 'release-common.ps1')
$releaseVersion = Get-ControllerReleaseVersion $Version
if ([string]::IsNullOrWhiteSpace($Tag)) {
    $Tag = "v$releaseVersion"
}
if ($Tag -cne "v$releaseVersion") {
    throw "Controller release tag must be v$releaseVersion. Use the separate keypad publisher for keypad releases."
}
if ([string]::IsNullOrWhiteSpace($NotesFile)) {
    $NotesFile = "public\docs\release-v$releaseVersion.md"
}
if ($WhatIfPreference -and -not $SkipBuild) {
    throw 'Use -SkipBuild -WhatIf to preview already packaged files without rebuilding.'
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
    if ([string]::IsNullOrWhiteSpace($Repository)) {
        if ($WhatIfPreference) {
            throw 'Specify -Repository owner/name when using -WhatIf for an offline preview.'
        }
        $repositoryOutput = & gh repo view `
            --json nameWithOwner `
            --jq ".nameWithOwner"
        if ($LASTEXITCODE -ne 0 -or
            [string]::IsNullOrWhiteSpace($repositoryOutput)) {
            throw "Could not determine the GitHub repository from the current checkout."
        }
        $Repository = ($repositoryOutput | Select-Object -First 1).Trim()
    }
    if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
        throw 'Repository must have the form owner/name.'
    }
    if (-not $WhatIfPreference) { Assert-ReleaseWorktree }

    if (-not $SkipBuild) {
        & (Join-Path $PSScriptRoot "package-release.ps1") `
            -Version $releaseVersion `
            -Runtime $Runtime
        if ($IncludeCompact) {
            & (Join-Path $PSScriptRoot "package-release.ps1") `
                -Version $releaseVersion `
                -Runtime $Runtime `
                -Compact
        }
    }
    & (Join-Path $PSScriptRoot 'verify-release.ps1') `
        -Version $releaseVersion -Runtime $Runtime -IncludeCompact:$IncludeCompact | Out-Host

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

    Write-Host "Repository: $Repository; tag: $Tag; notes: $notesPath"
    Write-Host "Draft: $Draft; prerelease: $Prerelease; replace assets: $ReplaceAssets"
    $releaseFiles | ForEach-Object { Write-Host "Asset: $_" }
    if (-not $PSCmdlet.ShouldProcess("$Repository release $Tag", 'Create or update release and upload the listed assets')) {
        return
    }
    Invoke-Checked "gh" @("auth", "status")
    Assert-ReleaseWorktree
    $head = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not resolve HEAD.' }
    $remoteTags = @(& git ls-remote --exit-code --tags `
        "https://github.com/$Repository.git" "refs/tags/$Tag" "refs/tags/$Tag^{}")
    if ($LASTEXITCODE -ne 0) { throw "Remote tag $Tag is missing or inaccessible in $Repository." }
    $tagLine = @($remoteTags | Where-Object { $_.EndsWith("refs/tags/$Tag^{}") })
    if ($tagLine.Count -eq 0) {
        $tagLine = @($remoteTags | Where-Object { $_.EndsWith("refs/tags/$Tag") })
    }
    if ($tagLine.Count -ne 1 -or ($tagLine[0] -split '\s+')[0] -ne $head) {
        throw "Remote tag $Tag does not point to current HEAD $head. Check out the release commit before uploading."
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
        $uploadArguments = @(
            "release", "upload", $Tag) +
            $releaseFiles +
            @(
                "--repo", $Repository)
        if ($ReplaceAssets) { $uploadArguments += "--clobber" }
        Invoke-Checked "gh" $uploadArguments
        Invoke-Checked "gh" $editArguments
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
