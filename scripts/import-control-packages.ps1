param([Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
[xml]$versions = Get-Content -LiteralPath (Join-Path $root 'Directory.Packages.props') -Raw
$version = [string]$versions.Project.PropertyGroup.CodexControlVersion
if (-not $version) { throw 'CodexControlVersion is missing.' }
$source = (Resolve-Path -LiteralPath $PackageDirectory).Path
$feed = Join-Path $root '.artifacts/control-packages'
$packages = foreach ($id in @('CodexControl', 'CodexControl.Windows')) {
    $name = "$id.$version.nupkg"
    $path = Join-Path $source $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing pinned package: $path" }
    $destination = Join-Path $feed $name
    if ((Test-Path -LiteralPath $destination) -and
        (Get-FileHash -LiteralPath $path).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) {
        throw "Different contents for existing package $name. Publish a new component version."
    }
    [pscustomobject]@{ Source=$path; Destination=$destination }
}
New-Item -ItemType Directory -Path $feed -Force | Out-Null
foreach ($package in $packages) {
    if (-not (Test-Path -LiteralPath $package.Destination)) {
        Copy-Item -LiteralPath $package.Source -Destination $package.Destination
    }
}
$solution = if (Test-Path -LiteralPath (Join-Path $root 'CodexMicro.slnx')) { 'CodexMicro.slnx' } else { 'AgentController.sln' }
dotnet restore (Join-Path $root $solution)
if ($LASTEXITCODE -ne 0) { throw 'Control package restore failed.' }
