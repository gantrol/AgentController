[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts\micro-lighting'
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

# The xUnit fixture owns the state -> expected color table. It verifies the
# exact production brush/opacity and classifies pixels rendered by the real
# WPF template, so a visually wrong hue fails before these previews are kept.
$previewVariables = [ordered]@{
    CODEX_MICRO_SCRIPTED_LIGHTING_MATRIX_PREVIEW_PATH =
        (Join-Path $OutputDirectory 'scripted-agent-key-matrix.png')
    CODEX_MICRO_PREVIEW_PATH =
        (Join-Path $OutputDirectory 'physical-surface-state-matrix.png')
    CODEX_MICRO_DEEPSEEK_PREVIEW_PATH =
        (Join-Path $OutputDirectory 'deepseek-running-blue.png')
    CODEX_MICRO_DEEPSEEK_IDLE_PREVIEW_PATH =
        (Join-Path $OutputDirectory 'deepseek-browser-waiting-idle.png')
}
$previousValues = @{}
foreach ($name in $previewVariables.Keys) {
    $previousValues[$name] = [Environment]::GetEnvironmentVariable($name)
    [Environment]::SetEnvironmentVariable($name, $previewVariables[$name])
}

try {
    $testProject = Join-Path $repositoryRoot `
        'virtual-micro\tests\CodexMicro.Desktop.Tests\CodexMicro.Desktop.Tests.csproj'
    $filter = 'FullyQualifiedName~AgentLightingVisualTests|' +
        'FullyQualifiedName~WindowDesignTests.KeyboardLayoutRendersOffscreenWithSquareKeycaps'
    & dotnet test $testProject -c $Configuration --filter $filter
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    foreach ($name in $previewVariables.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousValues[$name])
    }
}

Write-Host ''
Write-Host 'Micro lighting checks passed. Real-XAML previews:'
foreach ($path in $previewVariables.Values) {
    Write-Host "  $path"
}
