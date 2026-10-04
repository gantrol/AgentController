param(
    [Parameter(Mandatory, ParameterSetName='Controls')]
    [Parameter(Mandatory, ParameterSetName='Lights')][guid]$ThreadId,
    [Parameter(Mandatory, ParameterSetName='Controls')][guid]$SecondThreadId,
    [Parameter(Mandatory)][guid]$RestoreThreadId,
    [Parameter(ParameterSetName='Controls')][switch]$Driverless,
    [Parameter(Mandatory, ParameterSetName='Draft')][switch]$Draft,
    [Parameter(Mandatory, ParameterSetName='Lights')][switch]$Lights
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ($Driverless) {
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.WorkingDirectory = $repoRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['AGENTCONTROLLER_LIVE_THREAD'] = $ThreadId.ToString()
    $start.Environment['AGENTCONTROLLER_LIVE_SECOND_THREAD'] = $SecondThreadId.ToString()
    $start.Environment['AGENTCONTROLLER_LIVE_RESTORE_THREAD'] = $RestoreThreadId.ToString()
    $start.Environment['AGENTCONTROLLER_LIVE_REPORT'] = Join-Path $repoRoot '.artifacts/micro-e2e/driverless.json'
    foreach ($argument in @('test', (Join-Path $repoRoot 'app.Tests/AgentController.Tests.csproj'), '-c', 'Release',
        '--filter', 'Category=CodexLive', '--logger', 'trx;LogFileName=driverless.trx',
        '--results-directory', (Join-Path $repoRoot '.artifacts/micro-e2e'), '--nologo', '-v', 'minimal')) {
        $start.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        Write-Output $stdout.GetAwaiter().GetResult()
        Write-Output $stderr.GetAwaiter().GetResult()
        exit $process.ExitCode
    }
    finally { $process.Dispose() }
}
$project = Join-Path $repoRoot 'virtual-micro/tests/CodexMicro.LiveSmoke/CodexMicro.LiveSmoke.csproj'
dotnet build $project -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $repoRoot 'virtual-micro/tests/CodexMicro.LiveSmoke/bin/Release/net10.0-windows/CodexMicro.Desktop.Tests.exe'))
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$arguments = if ($Draft) { @('--draft', $RestoreThreadId.ToString(), (Join-Path $repoRoot '.artifacts/micro-e2e/draft.json')) }
elseif ($Lights) { @('--lights', $ThreadId.ToString(), $RestoreThreadId.ToString(), (Join-Path $repoRoot '.artifacts/micro-e2e/lights.json')) }
else { @($ThreadId.ToString(), $SecondThreadId.ToString(), $RestoreThreadId.ToString(), (Join-Path $repoRoot '.artifacts/micro-e2e/live.json')) }
foreach ($argument in $arguments) { $start.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::Start($start)
try {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    Write-Output $stdout.GetAwaiter().GetResult()
    Write-Output $stderr.GetAwaiter().GetResult()
    exit $process.ExitCode
} finally { $process.Dispose() }
