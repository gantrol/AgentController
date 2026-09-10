[CmdletBinding()]
param(
    [string]$Version,
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',
    [switch]$IncludeCompact,
    [switch]$SourceOnly,
    [switch]$RequireClean
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'release-common.ps1')
$Version = Get-ControllerReleaseVersion $Version
$notesRelative = "public/docs/release-v$Version.md"
$notesPath = Join-Path $repoRoot $notesRelative
if (-not (Test-Path -LiteralPath $notesPath -PathType Leaf) -or
    [string]::IsNullOrWhiteSpace((Get-Content -LiteralPath $notesPath -Raw -Encoding UTF8))) {
    throw "Write release notes first: $notesRelative"
}
foreach ($readme in @('README.md', 'README.zh-CN.md')) {
    $content = Get-Content -LiteralPath (Join-Path $repoRoot $readme) -Raw -Encoding UTF8
    if (-not $content.Contains("badge/version-$Version-blue")) {
        throw "Update the version badge in $readme to $Version."
    }
}
if ($RequireClean) {
    Push-Location $repoRoot
    try { Assert-ReleaseWorktree } finally { Pop-Location }
}
Write-Host "Source version and release notes: $Version"
if ($SourceOnly) { return }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$suffixes = @('')
if ($IncludeCompact) { $suffixes += '-compact' }
foreach ($suffix in $suffixes) {
    $packageName = "AgentController-$Version-$Runtime$suffix"
    $zipPath = Join-Path $repoRoot "dist\$packageName.zip"
    $hash = Get-ReleaseChecksum $zipPath
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        foreach ($required in @(
            'AgentController.exe', 'LICENSE',
            'THIRD-PARTY/CREATRBOI-White-XBOX-Controller-ATTRIBUTION.md',
            'THIRD-PARTY/CREATRBOI-White-XBOX-Controller-LICENSE.txt',
            'DOCS/README.md', 'DOCS/README.zh-CN.md', "DOCS/$notesRelative"
        )) {
            if ($entries -cnotcontains "$packageName/$required") {
                throw "Archive is missing $required : $zipPath"
            }
        }
    }
    finally { $archive.Dispose() }
    $sizeMiB = (Get-Item -LiteralPath $zipPath).Length / 1MB
    if ($suffix -eq '-compact' -and $sizeMiB -gt 30) {
        throw "Compact archive exceeds 30 MiB: $zipPath"
    }
    [pscustomobject]@{ File = [IO.Path]::GetFileName($zipPath); MiB = [Math]::Round($sizeMiB, 2); SHA256 = $hash }
}
