[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9._-]+$')]
    [string] $Distribution = 'Ubuntu',

    [string] $PythonPath = '',

    [string] $Model = '',

    [ValidateRange(1, 65535)]
    [int] $Port = 8765,

    [ValidateRange(0.05, 0.95)]
    [double] $GpuMemoryUtilization = 0.55,

    [ValidateRange(0.25, 10)]
    [double] $ChunkSeconds = 2.0,

    [ValidateRange(0, 0.25)]
    [double] $VoiceThreshold = 0.008
)

$ErrorActionPreference = 'Stop'
$serverScript = Join-Path $PSScriptRoot 'qwen3-asr-stream-server.py'
if (-not (Test-Path -LiteralPath $serverScript -PathType Leaf)) {
    throw "Bundled Qwen streaming server is missing: $serverScript"
}

$wsl = Join-Path $env:SystemRoot 'System32\wsl.exe'
if (-not (Test-Path -LiteralPath $wsl -PathType Leaf)) {
    throw 'WSL is not installed. Enable WSL and install an Ubuntu distribution first.'
}

Write-Output "[dsh-qwen-asr] Checking WSL distribution '$Distribution'…"
$wslHome = (& $wsl -d $Distribution --exec sh -lc 'printf %s "$HOME"').Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($wslHome)) {
    throw "Could not start WSL distribution '$Distribution'."
}

$linuxScript = (& $wsl -d $Distribution --exec wslpath -a $serverScript).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($linuxScript)) {
    throw 'Could not translate the bundled server path for WSL.'
}

if ([string]::IsNullOrWhiteSpace($PythonPath)) {
    $pythonCandidates = @(
        "$wslHome/.local/share/dsh-qwen-asr/venv/bin/python",
        "$wslHome/.local/share/catai-qwen-asr/venv/bin/python",
        '/usr/bin/python3'
    )
    foreach ($candidate in $pythonCandidates) {
        & $wsl -d $Distribution --exec test -x $candidate
        if ($LASTEXITCODE -eq 0) {
            $PythonPath = $candidate
            break
        }
    }
}

& $wsl -d $Distribution --exec test -x $PythonPath
if ($LASTEXITCODE -ne 0) {
    throw "Python was not found inside '$Distribution' at '$PythonPath'."
}

if ([string]::IsNullOrWhiteSpace($Model)) {
    $modelCandidates = @(
        "$wslHome/.local/share/dsh-qwen-asr/models/Qwen3-ASR-0.6B",
        "$wslHome/.local/share/catai-qwen-asr/models/Qwen3-ASR-0.6B"
    )
    foreach ($candidate in $modelCandidates) {
        & $wsl -d $Distribution --exec test -d $candidate
        if ($LASTEXITCODE -eq 0) {
            $Model = $candidate
            break
        }
    }
    if ([string]::IsNullOrWhiteSpace($Model)) {
        $Model = 'Qwen/Qwen3-ASR-0.6B'
    }
}

Write-Output '[dsh-qwen-asr] Checking Python packages (qwen-asr[vllm], aiohttp, numpy)…'
& $wsl -d $Distribution --exec $PythonPath -c 'import aiohttp, numpy, qwen_asr'
if ($LASTEXITCODE -ne 0) {
    throw "The selected Python environment is missing qwen-asr[vllm], aiohttp, or numpy."
}

Write-Output "[dsh-qwen-asr] Loading '$Model'; the first model download can take several minutes…"
& $wsl -d $Distribution --exec $PythonPath $linuxScript `
    --model $Model `
    --host 127.0.0.1 `
    --port $Port `
    --gpu-memory-utilization $GpuMemoryUtilization `
    --max-model-len 8192 `
    --max-num-seqs 1 `
    --chunk-seconds $ChunkSeconds `
    --voice-threshold $VoiceThreshold
exit $LASTEXITCODE
