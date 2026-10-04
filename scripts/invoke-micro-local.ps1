param(
    [Parameter(Mandatory)][string]$Tool,
    [string]$ArgumentsJson = '{}',
    [string]$Executable = '.artifacts/micro-e2e/publish/CodexMicro.Plugin.exe'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = [IO.Path]::GetFullPath((Join-Path $repoRoot $Executable))
$request = @{ jsonrpc='2.0'; id=1; method='tools/call'; params=@{ name=$Tool; arguments=($ArgumentsJson | ConvertFrom-Json) } } | ConvertTo-Json -Depth 15 -Compress
$start = [Diagnostics.ProcessStartInfo]::new($exe)
$start.ArgumentList.Add('--mcp')
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$process = [Diagnostics.Process]::Start($start)
try {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.StandardInput.WriteLine($request)
    $process.StandardInput.Close()
    if (!$process.WaitForExit(30000)) {
        $process.Kill()
        throw 'MCP timed out; operation outcome is unknown. Do not automatically retry.'
    }
    Write-Output $stdout.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) { throw $stderr.GetAwaiter().GetResult() }
} finally { $process.Dispose() }
