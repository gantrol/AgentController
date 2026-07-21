[CmdletBinding()]
param(
    [ValidateSet('osx-arm64', 'osx-x64')]
    [string[]] $Runtime = @('osx-arm64', 'osx-x64'),

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $OutputRoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot `
    'src\AgentController.Desktop\AgentController.Desktop.csproj'
$plistTemplate = Join-Path $repoRoot 'packaging\macos\Info.plist'
$shortVersion = '0.1.0'
$buildVersion = '1'
$executableName = 'AgentController.Desktop'

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot 'artifacts\macos'
}

$outputRootPath = [IO.Path]::GetFullPath($OutputRoot)
$outputRootWithSeparator = $outputRootPath.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar

foreach ($targetRuntime in $Runtime) {
    $bundlePath = [IO.Path]::GetFullPath(
        (Join-Path $outputRootPath `
            "$targetRuntime\Agent Controller.app"))
    if (-not $bundlePath.StartsWith(
            $outputRootWithSeparator,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to package outside the output root: $bundlePath"
    }

    if (Test-Path -LiteralPath $bundlePath) {
        Remove-Item -LiteralPath $bundlePath -Recurse -Force
    }

    $contentsPath = Join-Path $bundlePath 'Contents'
    $macOsPath = Join-Path $contentsPath 'MacOS'
    $resourcesPath = Join-Path $contentsPath 'Resources'
    New-Item -ItemType Directory -Path $macOsPath -Force | Out-Null
    New-Item -ItemType Directory -Path $resourcesPath -Force | Out-Null

    Write-Host "Publishing $targetRuntime..."
    & dotnet publish $project `
        --configuration $Configuration `
        --runtime $targetRuntime `
        --self-contained true `
        --no-restore `
        -p:UseAppHost=true `
        -p:PublishSingleFile=false `
        --output $macOsPath
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $targetRuntime."
    }

    $plist = (Get-Content `
        -LiteralPath $plistTemplate `
        -Raw `
        -Encoding UTF8).Replace(
            '__SHORT_VERSION__',
            $shortVersion).Replace(
                '__BUILD_VERSION__',
                $buildVersion)
    [IO.File]::WriteAllText(
        (Join-Path $contentsPath 'Info.plist'),
        $plist,
        [Text.UTF8Encoding]::new($false))

    $appHost = Join-Path $macOsPath $executableName
    $avaloniaNative = Join-Path $macOsPath 'libAvaloniaNative.dylib'
    foreach ($required in @($appHost, $avaloniaNative)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            throw "Incomplete macOS bundle; missing $required"
        }
    }

    $header = ([IO.File]::ReadAllBytes($appHost))[0..7]
    $expectedCpu = if ($targetRuntime -eq 'osx-arm64') {
        0x0100000C
    }
    else {
        0x01000007
    }
    $actualCpu = [BitConverter]::ToInt32($header, 4)
    if (
        $header[0] -ne 0xCF -or
        $header[1] -ne 0xFA -or
        $header[2] -ne 0xED -or
        $header[3] -ne 0xFE -or
        $actualCpu -ne $expectedCpu
    ) {
        throw "The app host does not match $targetRuntime."
    }

    Write-Host "Ready: $bundlePath"
    Write-Host "On macOS, run from the $targetRuntime folder:"
    Write-Host "  chmod +x 'Agent Controller.app/Contents/MacOS/$executableName'"
}

Write-Host 'Signing and notarization intentionally require a Mac and Developer ID credentials.'
