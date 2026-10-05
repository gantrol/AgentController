[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9.-]{3,50}$')][string]$IdentityName,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Publisher,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$PublisherDisplayName,
    [string]$DisplayName = 'Agent Controller',
    [string]$PackageVersion,
    [string]$OutputDirectory,
    [switch]$Candidate
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
if (-not $PackageVersion) {
    [xml]$versionFile = Get-Content -LiteralPath (Join-Path $root 'app/AgentController.csproj') -Raw
    $PackageVersion = [string]$versionFile.Project.PropertyGroup.FileVersion
}
if ($PackageVersion -notmatch '^\d+\.\d+\.\d+\.0$') {
    throw 'A Store version must have four numeric parts with a final zero.'
}
[version]$parsedVersion = $PackageVersion
if ($parsedVersion.Major -gt 65535 -or $parsedVersion.Minor -gt 65535 -or $parsedVersion.Build -gt 65535) {
    throw 'Each version component must be at most 65535.'
}
if (-not $Publisher.StartsWith('CN=', [StringComparison]::Ordinal)) {
    throw 'Use Package/Identity/Publisher from Partner Center, including CN=.'
}

$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
$sdk = Get-ChildItem -LiteralPath $sdkRoot -Directory |
    Where-Object { $_.Name -match '^10\.0\.\d+\.0$' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'x64/makeappx.exe')) } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $sdk) { throw 'Install the Windows SDK with MakeAppx and MakePri.' }
$makeAppx = Join-Path $sdk.FullName 'x64/makeappx.exe'
$makePri = Join-Path $sdk.FullName 'x64/makepri.exe'
if (-not (Test-Path -LiteralPath $makePri)) { throw "Missing SDK tool: $makePri" }

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $root "dist/store/$PackageVersion/$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ($output -match '(?i)trash') { throw 'Unsupported output path.' }
if (Test-Path -LiteralPath $output) { throw "Output directory already exists: $output" }
$payload = Join-Path $output 'payload'
$images = Join-Path $payload 'Assets'
New-Item -ItemType Directory -Path $images -Force | Out-Null

# Keep the runtime in the package; installation must not download .NET.
dotnet publish (Join-Path $root 'app/AgentController.csproj') `
    -c Release -r win-x64 --self-contained true -o $payload `
    --artifacts-path (Join-Path $output 'dotnet') -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $payload

# Derive Windows package-size variants from the existing product icon.
Add-Type -AssemblyName System.Drawing
$source = [Drawing.Image]::FromFile((Join-Path $root 'app/Assets/AgentController.png'))
try {
    foreach ($asset in @(
        @{ Name='StoreLogo.png'; Size=50 },
        @{ Name='Square44x44Logo.png'; Size=44 },
        @{ Name='Square150x150Logo.png'; Size=150 },
        @{ Name='Square44x44Logo.targetsize-44_altform-unplated.png'; Size=44 },
        @{ Name='AppTile300.png'; Size=300 }
    )) {
        $bitmap = [Drawing.Bitmap]::new($asset.Size, $asset.Size)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($source, 0, 0, $asset.Size, $asset.Size)
            $bitmap.Save((Join-Path $images $asset.Name), [Drawing.Imaging.ImageFormat]::Png)
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
} finally { $source.Dispose() }

function Escape-Xml([string]$value) { [Security.SecurityElement]::Escape($value) }
$xmlName = Escape-Xml $IdentityName
$xmlPublisher = Escape-Xml $Publisher
$xmlPublisherDisplayName = Escape-Xml $PublisherDisplayName
$xmlDisplayName = Escape-Xml $DisplayName
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
         xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10"
         xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
         IgnorableNamespaces="uap desktop rescap">
  <Identity Name="$xmlName" Publisher="$xmlPublisher" Version="$PackageVersion" ProcessorArchitecture="x64" />
  <Properties>
    <DisplayName>$xmlDisplayName</DisplayName>
    <PublisherDisplayName>$xmlPublisherDisplayName</PublisherDisplayName>
    <Description>Gamepad controls for Codex and DeepSeek Harness.</Description>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Resources><Resource Language="en-US" /><Resource Language="zh-CN" /></Resources>
  <Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" /></Dependencies>
  <Applications>
    <Application Id="AgentController" Executable="AgentController.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="$xmlDisplayName" Description="Gamepad controls for Codex and DeepSeek Harness."
          Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png" BackgroundColor="transparent" />
      <Extensions>
        <desktop:Extension Category="windows.startupTask" Executable="AgentController.exe" EntryPoint="Windows.FullTrustApplication">
          <desktop:StartupTask TaskId="AgentControllerStartup" Enabled="false" DisplayName="$xmlDisplayName" />
        </desktop:Extension>
      </Extensions>
    </Application>
  </Applications>
  <Capabilities><rescap:Capability Name="runFullTrust" /></Capabilities>
</Package>
"@
Set-Content -LiteralPath (Join-Path $payload 'AppxManifest.xml') -Value $manifest -Encoding utf8
$priConfig = Join-Path $output 'priconfig.xml'
& $makePri createconfig /cf $priConfig /dq en-US /o
if ($LASTEXITCODE -ne 0) { throw 'MakePri configuration failed.' }
& $makePri new /pr $payload /cf $priConfig /of (Join-Path $payload 'resources.pri') /o
if ($LASTEXITCODE -ne 0) { throw 'MakePri indexing failed.' }

$suffix = if ($Candidate) { '-candidate' } else { '' }
$package = Join-Path $output "AgentController-$PackageVersion-x64$suffix.msix"
& $makeAppx pack /d $payload /p $package /o
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx validation or packaging failed.' }
$hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
Set-Content -LiteralPath "$package.sha256" -Value "$hash  $([IO.Path]::GetFileName($package))" -Encoding ascii
[ordered]@{
    identityName=$IdentityName; publisher=$Publisher; publisherDisplayName=$PublisherDisplayName
    displayName=$DisplayName; packageVersion=$PackageVersion; architecture='x64'; selfContained=$true
    candidate=[bool]$Candidate; signed=$false; package=$package; sha256=$hash
    sdk=$sdk.Name; sourceCommit=(git -C $root rev-parse HEAD)
    sourceHasChanges=[bool](git -C $root status --porcelain)
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'package-info.json') -Encoding utf8
Write-Output "MSIX: $package"
Write-Output 'Unsigned package: submit to Partner Center for Store signing; do not distribute as a signed installer.'
