$ErrorActionPreference = 'Stop'

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $workspaceRoot 'app/AgentController.csproj'
$artifactsPath = Join-Path $workspaceRoot 'artifacts/controller-storybook'

& dotnet build $projectPath -c Debug --artifacts-path $artifactsPath --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

# The console host keeps startup output and unhandled exceptions visible.
$galleryAssembly = Join-Path $artifactsPath 'bin/AgentController/debug/AgentController.dll'
& dotnet $galleryAssembly --dev-instance --component-gallery
exit $LASTEXITCODE
