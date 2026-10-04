[CmdletBinding()]
param([ValidateSet('legacy', 'software', 'both')][string]$Transport = 'both')

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repoRoot 'virtual-micro/tests/CodexMicro.Desktop.Tests/CodexMicro.Desktop.Tests.csproj'
$resultsRoot = Join-Path $repoRoot '.artifacts/micro-behavior-acceptance'
New-Item -ItemType Directory -Path $resultsRoot -Force | Out-Null
$variants = if ($Transport -eq 'both') { @('legacy', 'software') } else { @($Transport) }
$runs = @()
foreach ($variant in $variants) {
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.WorkingDirectory = $repoRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['MICRO_ACCEPTANCE_TRANSPORT'] = $variant
    foreach ($argument in @('test', $project, '-c', 'Release', '--filter', 'Category=BehaviorAcceptance',
        '--logger', "trx;LogFileName=$variant.trx", '--results-directory', $resultsRoot, '--nologo', '-v', 'quiet')) {
        $start.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        $output | Set-Content -LiteralPath (Join-Path $resultsRoot "$variant.log") -Encoding utf8
        Write-Output "$variant : exit $($process.ExitCode); $resultsRoot\$variant.trx"
        $output -split "`r?`n" | Where-Object { $_ -match '^(Passed!|Failed!|.*: error )' } | Write-Output
        $runs += [ordered]@{ transport = $variant; exitCode = $process.ExitCode; result = "$variant.trx" }
    } finally { $process.Dispose() }
}
$sources = @(
    'virtual-micro/src/CodexMicro.Desktop/Services/VirtualMicroBroker.cs',
    'virtual-micro/src/AgentController.MicroSurface.Wpf/SoftwareControl/SoftwareMicroTransport.cs',
    'virtual-micro/tests/CodexMicro.Desktop.Tests/BehaviorAcceptanceTests.cs',
    'virtual-micro/tests/CodexMicro.Desktop.Tests/BehaviorAcceptanceRig.cs',
    'virtual-micro/tests/CodexMicro.Desktop.Tests/PanelAcceptanceTests.cs'
)
$fingerprints = foreach ($source in $sources) {
    [ordered]@{ path = $source; sha256 = (Get-FileHash -LiteralPath (Join-Path $repoRoot $source) -Algorithm SHA256).Hash }
}
[ordered]@{
    timeUtc = [DateTime]::UtcNow.ToString('O')
    head = (& git -C $repoRoot rev-parse HEAD)
    baselineKind = 'Retained legacy transport; isolated external receiver; not a historical executable or full Codex E2E'
    sources = $fingerprints
    runs = $runs
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $resultsRoot 'run.json') -Encoding utf8
if ($runs.Where({ $_.exitCode -ne 0 }).Count -gt 0) { exit 1 }
exit 0
