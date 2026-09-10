[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('Check', 'Build', 'BackupSettings')]
    [string]$Action = 'Check',
    [switch]$RequireClean
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'release-common.ps1')

function Invoke-MaintenanceCommand([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE." }
}

Push-Location $repoRoot
try {
    if ($Action -eq 'BackupSettings') {
        $settingsRoot = [Environment]::GetFolderPath('LocalApplicationData')
        if ([string]::IsNullOrWhiteSpace($settingsRoot)) { throw 'LocalApplicationData is unavailable.' }
        $relativePaths = @(
            'AgentController\settings.json',
            'CodexController\settings.json',
            'CodexMicro\settings.json',
            'CodexMicro\micro-profile.json'
        )
        $keypadsPath = Join-Path $settingsRoot 'CodexMicro\keypads'
        if (Test-Path -LiteralPath $keypadsPath -PathType Container) {
            $relativePaths += @(Get-ChildItem -LiteralPath $keypadsPath -File -Filter '*.json' |
                Where-Object { $_.BaseName -match '^[0-9a-fA-F]{32}$' } |
                ForEach-Object { 'CodexMicro\keypads\' + $_.Name })
        }
        $available = @($relativePaths | Where-Object {
            Test-Path -LiteralPath (Join-Path $settingsRoot $_) -PathType Leaf
        })
        if ($available.Count -eq 0) {
            Write-Host 'No application settings found; no backup created.'
            return
        }
        $backupName = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N')
        $backupRoot = Join-Path $repoRoot ".artifacts\maintenance\backups\$backupName"
        if ($PSCmdlet.ShouldProcess($backupRoot, "Back up $($available.Count) settings files")) {
            $manifest = foreach ($relative in $available) {
                $source = Join-Path $settingsRoot $relative
                $destination = Join-Path $backupRoot $relative
                New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
                Copy-Item -LiteralPath $source -Destination $destination
                [pscustomobject]@{
                    Path = $relative
                    SHA256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
                }
            }
            ConvertTo-Json -InputObject @($manifest) -Depth 3 |
                Set-Content -LiteralPath (Join-Path $backupRoot 'manifest.json') -Encoding UTF8
            Write-Host "Backup: $backupRoot"
        }
        return
    }

    foreach ($command in @('git', 'dotnet')) {
        if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
            throw "Required command not found: $command"
        }
    }
    $sdk = Get-Content -LiteralPath 'global.json' -Raw -Encoding UTF8 | ConvertFrom-Json
    Write-Host "SDK requested: $($sdk.sdk.version), rollForward=$($sdk.sdk.rollForward)"
    Invoke-MaintenanceCommand 'dotnet' @('--version')
    Write-Host "Controller version: $(Get-ControllerReleaseVersion)"
    Invoke-MaintenanceCommand 'git' @('rev-parse', 'HEAD')
    Invoke-MaintenanceCommand 'git' @('status', '--short')
    if ($RequireClean) { Assert-ReleaseWorktree }
    $githubCli = Get-Command gh -ErrorAction SilentlyContinue
    Write-Host "GitHub CLI available: $($null -ne $githubCli) (needed only for upload)"

    if ($Action -eq 'Build' -and $PSCmdlet.ShouldProcess('AgentController.sln', 'Restore and build Release')) {
        Invoke-MaintenanceCommand 'dotnet' @('restore', 'AgentController.sln')
        Invoke-MaintenanceCommand 'dotnet' @('build', 'AgentController.sln', '-c', 'Release', '--no-restore')
    }
}
finally {
    Pop-Location
}
