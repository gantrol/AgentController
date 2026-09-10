Set-StrictMode -Version Latest

function Get-ControllerReleaseVersion([string]$Version) {
    $projectPath = Join-Path $PSScriptRoot '..\app\AgentController.csproj'
    [xml]$project = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
    $sourceVersion = $project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = $sourceVersion
    }
    $Version = $Version -creplace '^v', ''
    if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') {
        throw 'Version must have the form 1.2.3 or 1.2.3-rc.1 (optional v prefix).'
    }
    if ($Version -cne $sourceVersion) {
        throw "Requested version $Version differs from app/AgentController.csproj ($sourceVersion). Update the source version first."
    }
    $assemblyVersion = ($Version -split '-', 2)[0] + '.0'
    foreach ($name in @('AssemblyVersion', 'FileVersion', 'InformationalVersion')) {
        $expected = if ($name -eq 'InformationalVersion') { $Version } else { $assemblyVersion }
        $node = $project.SelectSingleNode("/Project/PropertyGroup/$name")
        if ($null -eq $node -or $node.InnerText -cne $expected) {
            throw "app/AgentController.csproj must set $name to $expected."
        }
    }
    return $Version
}

function Get-ReleaseChecksum([string]$ArchivePath) {
    foreach ($path in @($ArchivePath, "$ArchivePath.sha256")) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required release file is missing: $path"
        }
    }
    $lines = @(Get-Content -LiteralPath "$ArchivePath.sha256" -Encoding ascii |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($lines.Count -ne 1 -or $lines[0] -notmatch '^(?<hash>[0-9a-fA-F]{64})\s+\*?(?<file>.+)$') {
        throw "Invalid SHA-256 file: $ArchivePath.sha256"
    }
    $declaredHash = $Matches.hash
    $declaredFile = $Matches.file
    $actualHash = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash
    if ($actualHash -ine $declaredHash -or
        $declaredFile -cne [IO.Path]::GetFileName($ArchivePath)) {
        throw "SHA-256 verification failed: $ArchivePath"
    }
    return $actualHash.ToLowerInvariant()
}

function Assert-ReleaseWorktree {
    $changes = @(& git status --porcelain --untracked-files=normal)
    if ($LASTEXITCODE -ne 0) { throw 'Could not read Git worktree status.' }
    if ($changes.Count -gt 0) {
        throw 'Commit or move pending changes before releasing. Run git status --short to inspect them.'
    }
}
