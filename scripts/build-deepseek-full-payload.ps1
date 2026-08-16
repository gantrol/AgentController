[CmdletBinding()]
param(
    [string]$Version = "0.2.4",
    [string]$DistributionSource = "Ubuntu-24.04",
    [string]$BuildDistributionName = "CodexMicro-DeepSeek-Build-v024",
    [string]$VerifyDistributionName = "CodexMicro-DeepSeek-Verify-v024",
    [string]$OutputPath,
    [switch]$KeepBuildDistributions,
    [switch]$SkipRuntimeProbe
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$workRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $repoRoot ".artifacts\deepseek-full\$Version"))
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $workRoot "deepseek-runtime.wsl"
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$buildRoot = Join-Path $workRoot "build-distro"
$verifyRoot = Join-Path $workRoot "verify-distro"
$installerPath = Join-Path $repoRoot `
    "micro-bridge\DeepSeekHarness\scripts\install-dsh-wsl-runtime.sh"
$preparePath = Join-Path $repoRoot "scripts\prepare-deepseek-full-rootfs.sh"
$auditPath = Join-Path $repoRoot "scripts\audit-deepseek-full-rootfs.sh"
$pluginRoot = Join-Path $repoRoot "micro-bridge\DeepSeekHarness"
$wsl = Join-Path $env:WINDIR "System32\wsl.exe"

function Assert-RepositoryChild([string]$Path) {
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

function Get-WslDistributionNames {
    $raw = (& $wsl --list --quiet 2>&1 | Out-String) -replace "`0", ""
    if ($LASTEXITCODE -ne 0) {
        throw "Could not list WSL distributions: $($raw.Trim())"
    }
    return @($raw -split "`r?`n" |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_.Length -ne 0 })
}

function Assert-BuildDistributionName([string]$Name, [string]$Kind) {
    $expectedPrefix = "CodexMicro-DeepSeek-$Kind-"
    if (-not $Name.StartsWith(
            $expectedPrefix,
            [System.StringComparison]::Ordinal) -or
        $Name -notmatch '^[A-Za-z0-9._-]+$') {
        throw "Unsafe temporary WSL distribution name: $Name"
    }
}

function Invoke-WslChecked(
    [string[]]$Arguments,
    [string]$Description) {
    & $wsl @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Convert-ToWslPath([string]$Distribution, [string]$WindowsPath) {
    $raw = (& $wsl --distribution $Distribution --user root --exec `
        wslpath -a -u ([System.IO.Path]::GetFullPath($WindowsPath)) 2>&1 |
        Out-String) -replace "`0", ""
    if ($LASTEXITCODE -ne 0) {
        throw "Could not convert a Windows path inside $Distribution."
    }
    $candidate = @($raw -split "`r?`n" |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_.StartsWith('/') } |
        Select-Object -Last 1)
    if ($candidate.Count -ne 1) {
        throw "WSL returned no usable path for $WindowsPath."
    }
    return $candidate[0]
}

function Find-ProbePort {
    foreach ($port in 31900..31950) {
        $listener = [System.Net.Sockets.TcpListener]::new(
            [System.Net.IPAddress]::Loopback,
            $port)
        try {
            $listener.Start()
            return $port
        }
        catch [System.Net.Sockets.SocketException] {
            continue
        }
        finally {
            $listener.Stop()
        }
    }
    throw "No loopback port is available for the payload verification."
}

foreach ($path in @($workRoot, $OutputPath, $buildRoot, $verifyRoot)) {
    Assert-RepositoryChild $path
}
Assert-BuildDistributionName $BuildDistributionName "Build"
Assert-BuildDistributionName $VerifyDistributionName "Verify"
if ($BuildDistributionName -eq $VerifyDistributionName) {
    throw "Build and verification distribution names must differ."
}
foreach ($required in @($wsl, $installerPath, $preparePath, $auditPath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required build input is missing: $required"
    }
}

$existing = Get-WslDistributionNames
foreach ($name in @($BuildDistributionName, $VerifyDistributionName)) {
    if ($existing -contains $name) {
        throw "Temporary WSL distribution already exists; refusing to reuse it: $name"
    }
}

if (Test-Path -LiteralPath $workRoot) {
    Remove-Item -LiteralPath $workRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $workRoot -Force | Out-Null

Push-Location $pluginRoot
try {
    & pnpm run build
    if ($LASTEXITCODE -ne 0) {
        throw "DeepSeek bridge build failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$buildCreated = $false
$verifyCreated = $false
$serviceProcess = $null
try {
    Invoke-WslChecked @(
        "--install", $DistributionSource,
        "--name", $BuildDistributionName,
        "--location", $buildRoot,
        "--version", "2",
        "--no-launch",
        "--web-download") "Clean Ubuntu installation"
    $buildCreated = $true

    $installerWslPath = Convert-ToWslPath $BuildDistributionName $installerPath
    $installOutput = (& $wsl `
        --distribution $BuildDistributionName `
        --user root `
        --exec env `
        CODEX_MICRO_DSH_USER=codexmicro `
        bash $installerWslPath 2>&1 | Out-String) -replace "`0", ""
    Write-Host $installOutput.Trim()
    if ($LASTEXITCODE -ne 0 -or $installOutput -notmatch 'managed-ready=1') {
        throw "DeepSeek runtime provisioning failed in the clean distribution."
    }

    $prepareWslPath = Convert-ToWslPath $BuildDistributionName $preparePath
    $prepareOutput = (& $wsl `
        --distribution $BuildDistributionName `
        --user root `
        --exec env `
        CODEX_MICRO_DSH_USER=codexmicro `
        CODEX_MICRO_RELEASE_VERSION=$Version `
        bash $prepareWslPath 2>&1 | Out-String) -replace "`0", ""
    Write-Host $prepareOutput.Trim()
    if ($LASTEXITCODE -ne 0 -or $prepareOutput -notmatch 'rootfs-ready=1') {
        throw "DeepSeek rootfs finalization failed."
    }

    Invoke-WslChecked @("--terminate", $BuildDistributionName) `
        "Build distribution termination"
    Invoke-WslChecked @(
        "--export", $BuildDistributionName, $OutputPath,
        "--format", "tar.gz") "WSL payload export"
    if (-not (Test-Path -LiteralPath $OutputPath -PathType Leaf)) {
        throw "WSL export did not produce $OutputPath."
    }
    $magic = [System.IO.File]::ReadAllBytes($OutputPath)[0..1]
    if ($magic[0] -ne 0x1f -or $magic[1] -ne 0x8b) {
        throw "The .wsl payload is not gzip-compressed."
    }

    $auditWslPath = Convert-ToWslPath $BuildDistributionName $auditPath
    $payloadWslPath = Convert-ToWslPath $BuildDistributionName $OutputPath
    $auditOutput = (& $wsl `
        --distribution $BuildDistributionName `
        --user root `
        --exec bash $auditWslPath $payloadWslPath 2>&1 |
        Out-String) -replace "`0", ""
    Write-Host $auditOutput.Trim()
    if ($LASTEXITCODE -ne 0 -or $auditOutput -notmatch 'rootfs-audit=ready') {
        throw "Exported DeepSeek rootfs did not pass the security audit."
    }
    Invoke-WslChecked @("--terminate", $BuildDistributionName) `
        "Post-audit build distribution termination"

    Invoke-WslChecked @(
        "--install", "--from-file", $OutputPath,
        "--name", $VerifyDistributionName,
        "--location", $verifyRoot,
        "--no-launch") "Bundled WSL payload import"
    $verifyCreated = $true

    $verifyInstallerWslPath = Convert-ToWslPath `
        $VerifyDistributionName $installerPath
    $verifyOutput = (& $wsl `
        --distribution $VerifyDistributionName `
        --user root `
        --exec env `
        CODEX_MICRO_DSH_USER=codexmicro `
        bash $verifyInstallerWslPath 2>&1 | Out-String) -replace "`0", ""
    Write-Host $verifyOutput.Trim()
    if ($LASTEXITCODE -ne 0 -or
        $verifyOutput -notmatch 'runtime-source=bundled-or-cached' -or
        $verifyOutput -notmatch 'managed-ready=1') {
        throw "Imported payload did not pass the strict offline installer path."
    }

    if (-not $SkipRuntimeProbe) {
        $port = Find-ProbePort
        $stdoutPath = Join-Path $workRoot "verify-dsh.stdout.log"
        $stderrPath = Join-Path $workRoot "verify-dsh.stderr.log"
        $serviceProcess = Start-Process `
            -FilePath $wsl `
            -ArgumentList @(
                "--distribution", $VerifyDistributionName,
                "--user", "codexmicro",
                "--exec",
                "/home/codexmicro/.local/share/codex-micro/deepseek/bin/start-dsh-wsl.sh",
                "--port", "$port") `
            -RedirectStandardOutput $stdoutPath `
            -RedirectStandardError $stderrPath `
            -WindowStyle Hidden `
            -PassThru
        $ready = $false
        $deadline = [DateTime]::UtcNow.AddMinutes(2)
        $controlUri = "http://127.0.0.1:$port/__agentcontroller/micro/request"
        while ([DateTime]::UtcNow -lt $deadline -and -not $serviceProcess.HasExited) {
            try {
                $response = Invoke-RestMethod `
                    -Uri $controlUri `
                    -Method Post `
                    -ContentType "application/json" `
                    -Body '{"version":1,"source":"codex-micro","action":"state/read"}' `
                    -TimeoutSec 2
                if ($response.success -eq $true) {
                    $ready = $true
                    break
                }
            }
            catch {
                Start-Sleep -Milliseconds 750
            }
        }
        if (-not $ready) {
            $detail = if (Test-Path -LiteralPath $stderrPath) {
                Get-Content -LiteralPath $stderrPath -Raw
            }
            else {
                "No Harness error log was produced."
            }
            throw "Bundled Harness did not expose the Micro bridge: $detail"
        }
        Write-Host "runtime-probe=ready"
        Write-Host "runtime-probe-uri=$controlUri"
    }

    $hash = Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256
    Write-Host ("Payload: {0} ({1:N2} MiB)" -f `
        $OutputPath, ((Get-Item -LiteralPath $OutputPath).Length / 1MB))
    Write-Host "Payload SHA256: $($hash.Hash.ToLowerInvariant())"
}
finally {
    if ($null -ne $serviceProcess -and -not $serviceProcess.HasExited) {
        Stop-Process -Id $serviceProcess.Id -Force -ErrorAction SilentlyContinue
    }
    foreach ($entry in @(
            @{ Name = $VerifyDistributionName; Created = $verifyCreated },
            @{ Name = $BuildDistributionName; Created = $buildCreated })) {
        if ($entry.Created) {
            & $wsl --terminate $entry.Name 2>$null
            if (-not $KeepBuildDistributions) {
                & $wsl --unregister $entry.Name
            }
        }
    }
}
