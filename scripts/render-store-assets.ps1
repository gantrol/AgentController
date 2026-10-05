[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) {
    [xml]$project = Get-Content -LiteralPath (Join-Path $root 'app/AgentController.csproj') -Raw
    $version = [string]$project.Project.PropertyGroup.Version
    $OutputDirectory = Join-Path $root "dist/store/listing/$version"
}
if ($OutputDirectory -match '(?i)trash') { throw 'Unsupported output path.' }
dotnet run --project (Join-Path $root 'scripts/StoreAssets/StoreAssets.csproj') -c Release -- $root $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Store asset export failed.' }
