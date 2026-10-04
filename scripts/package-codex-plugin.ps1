param([switch]$Install)

throw 'Codex Micro moved to the standalone codex-micro-monitor repository. Use its scripts/package.ps1.'

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$pluginRoot = Join-Path $repoRoot 'plugins\codex-micro-keypad'
$project = Join-Path $repoRoot 'micro-bridge\CodexPlugin\CodexMicro.Plugin.csproj'
$manifest = Get-Content -LiteralPath (Join-Path $pluginRoot 'plugin.json') -Raw | ConvertFrom-Json
$publishRoot = Join-Path $pluginRoot 'bin'
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $pluginRoot 'LICENSE') -Force

& dotnet publish $project -c Release -r win-x64 --self-contained false -o $publishRoot
if ($LASTEXITCODE -ne 0) { throw 'Codex Micro plugin publish failed.' }

$obsoleteVoiceRoot = [System.IO.Path]::GetFullPath((Join-Path $publishRoot 'voice'))
$resolvedPublishRoot = [System.IO.Path]::GetFullPath($publishRoot).TrimEnd('\') + '\'
if (!$obsoleteVoiceRoot.StartsWith($resolvedPublishRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Voice cleanup path is outside the plugin publish directory.'
}
if (Test-Path -LiteralPath $obsoleteVoiceRoot) {
    Remove-Item -LiteralPath $obsoleteVoiceRoot -Recurse -Force
}
Get-ChildItem -LiteralPath $publishRoot -File -Filter '*.pdb' | Remove-Item -Force

$distRoot = Join-Path $repoRoot 'dist'
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
$archive = Join-Path $distRoot "Codex-Micro-Keypad-Plugin-v$($manifest.version)-win-x64.zip"
$files = @('plugin.json', 'mcp.json', 'LICENSE', 'skills\keypad\SKILL.md')
$files += Get-ChildItem -LiteralPath $publishRoot -File -Recurse |
    Where-Object { $_.FullName -notmatch '(?i)trash' -and $_.Extension -ne '.pdb' } |
    Sort-Object FullName |
    ForEach-Object { [System.IO.Path]::GetRelativePath($pluginRoot, $_.FullName) }
Add-Type -AssemblyName System.IO.Compression
$stream = [System.IO.File]::Open($archive, [System.IO.FileMode]::Create)
$zip = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($relative in $files) {
        $source = Join-Path $pluginRoot $relative
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing plugin file: $relative" }
        $entry = $zip.CreateEntry('codex-micro-keypad/' + $relative.Replace('\', '/'))
        $inputStream = [System.IO.File]::OpenRead($source)
        $outputStream = $entry.Open()
        try { $inputStream.CopyTo($outputStream) }
        finally { $outputStream.Dispose(); $inputStream.Dispose() }
    }
} finally { $zip.Dispose(); $stream.Dispose() }

$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText($archive + '.sha256', "$hash  $([System.IO.Path]::GetFileName($archive))`n")
Write-Output $archive

if ($Install) {
    & codex --enable plugins plugin marketplace add $repoRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not register the local plugin marketplace.' }
    & codex --enable plugins plugin add 'codex-micro-keypad@agent-controller' --json
    if ($LASTEXITCODE -ne 0) { throw 'Could not install the Codex Micro plugin.' }
}
