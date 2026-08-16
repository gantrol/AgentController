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
    [double] $ChunkSeconds = 1.0,

    [ValidateRange(0, 0.25)]
    [double] $VoiceThreshold = 0.008,

    [switch] $CheckOnly
)

$ErrorActionPreference = 'Stop'
$serverScript = Join-Path $PSScriptRoot 'qwen3-asr-stream-server.py'
if (-not (Test-Path -LiteralPath $serverScript -PathType Leaf)) {
    throw "Bundled Qwen streaming server is missing: $serverScript"
}

$wslCommand = Get-Command wsl.exe -ErrorAction SilentlyContinue
if ($null -eq $wslCommand) {
    throw 'WSL is not installed. Enable WSL and install an Ubuntu distribution first.'
}
$wsl = $wslCommand.Source

Write-Output "[codex-micro-qwen-asr] Checking WSL distribution '$Distribution'…"
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
        "$wslHome/.local/share/codex-micro/qwen-asr/venv/bin/python",
        "$wslHome/.venvs/qwen-asr/bin/python"
    )
    foreach ($candidate in $pythonCandidates) {
        & $wsl -d $Distribution --exec test -x $candidate
        if ($LASTEXITCODE -eq 0) {
            $PythonPath = $candidate
            break
        }
    }
    if ([string]::IsNullOrWhiteSpace($PythonPath)) {
        $PythonPath = (& $wsl -d $Distribution --exec sh -lc `
            'for p in "$HOME"/.local/share/*qwen-asr/venv/bin/python; do [ -x "$p" ] && { printf %s "$p"; break; }; done').Trim()
    }
    if ([string]::IsNullOrWhiteSpace($PythonPath)) {
        $PythonPath = (& $wsl -d $Distribution --exec sh -lc `
            'command -v python3 || command -v python').Trim()
    }
}

if ([string]::IsNullOrWhiteSpace($PythonPath)) {
    throw "Python was not found inside '$Distribution'."
}
& $wsl -d $Distribution --exec test -x $PythonPath
if ($LASTEXITCODE -ne 0) {
    throw "Python was not found inside '$Distribution' at '$PythonPath'."
}

if ([string]::IsNullOrWhiteSpace($Model) -or
    $Model -eq 'Qwen/Qwen3-ASR-0.6B') {
    $modelCandidates = @(
        "$wslHome/.local/share/codex-micro/qwen-asr/models/Qwen3-ASR-0.6B"
    )
    foreach ($candidate in $modelCandidates) {
        & $wsl -d $Distribution --exec test -d $candidate
        if ($LASTEXITCODE -eq 0) {
            $Model = $candidate
            break
        }
    }
    if ([string]::IsNullOrWhiteSpace($Model)) {
        $Model = (& $wsl -d $Distribution --exec sh -lc `
            'for p in "$HOME"/.local/share/*qwen-asr/models/Qwen3-ASR-0.6B; do [ -d "$p" ] && { printf %s "$p"; break; }; done').Trim()
    }
    if ([string]::IsNullOrWhiteSpace($Model)) {
        $Model = 'Qwen/Qwen3-ASR-0.6B'
    }
}

Write-Output '[codex-micro-qwen-asr] Checking qwen-asr[vllm], aiohttp, and numpy…'
& $wsl -d $Distribution --exec $PythonPath -c `
    'import aiohttp, numpy, qwen_asr, vllm'
if ($LASTEXITCODE -ne 0) {
    throw 'The selected Python environment is missing qwen-asr[vllm], aiohttp, numpy, or vllm.'
}

if ($CheckOnly) {
    Write-Output "[codex-micro-qwen-asr] Python: $PythonPath"
    Write-Output "[codex-micro-qwen-asr] Model: $Model"
    Write-Output "[codex-micro-qwen-asr] Endpoint: ws://127.0.0.1:$Port/v1/stream"
    Write-Output '[codex-micro-qwen-asr] Environment check passed; service was not started.'
    exit 0
}

Write-Output "[codex-micro-qwen-asr] Loading '$Model'; the first model download can take several minutes…"
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
