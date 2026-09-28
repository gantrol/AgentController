param(
    [string]$XamlPath = (Join-Path $PSScriptRoot '..\Assets\LightingStudies\mint-keyglow.xaml'),
    [double]$Scale = 2
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
[System.Windows.Media.RenderOptions]::ProcessRenderMode = [System.Windows.Interop.RenderMode]::SoftwareOnly

$sourcePath = (Resolve-Path -LiteralPath $XamlPath).Path
$outputDirectory = Split-Path -Parent $sourcePath
$source = [System.IO.File]::ReadAllText($sourcePath)
$renders = @{}
$surfaces = @{}
$variants = @(
    @{ Name = 'current'; Intensity = '0'; OriginalDot = '0.6'; MintDot = '0'; FileName = 'current-keyglow.png' },
    @{ Name = 'mint-original-center'; Intensity = '1'; OriginalDot = '0.6'; MintDot = '0'; FileName = 'mint-keyglow-original-center.png' },
    @{ Name = 'mint'; Intensity = '1'; OriginalDot = '0'; MintDot = '1'; FileName = 'mint-keyglow.png' }
)

function Write-Png {
    param([System.Windows.Media.Imaging.BitmapSource]$Bitmap, [string]$FilePath)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($Bitmap))
    $stream = [System.IO.File]::Open($FilePath, [System.IO.FileMode]::Create)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
    Write-Output $FilePath
}

foreach ($variant in $variants) {
    $document = [System.Xml.XmlDocument]::new()
    $document.LoadXml($source)
    $namespaces = [System.Xml.XmlNamespaceManager]::new($document.NameTable)
    $namespaces.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')
    $intensity = $document.SelectSingleNode('//*[@x:Key="MintPreviewIntensity"]', $namespaces)
    $intensity.InnerText = $variant.Intensity
    $originalDot = $document.SelectSingleNode('//*[@x:Key="OriginalCurrentDotOpacity"]', $namespaces)
    $originalDot.InnerText = $variant.OriginalDot
    $mintDot = $document.SelectSingleNode('//*[@x:Key="MintCurrentDotOpacity"]', $namespaces)
    $mintDot.InnerText = $variant.MintDot

    $reader = [System.Xml.XmlNodeReader]::new($document)
    try { $surface = [System.Windows.Markup.XamlReader]::Load($reader) }
    finally { $reader.Dispose() }
    $size = [System.Windows.Size]::new($surface.Width, $surface.Height)
    $surface.Measure($size)
    $surface.Arrange([System.Windows.Rect]::new($size))
    $surface.UpdateLayout()

    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(
        [int]($size.Width * $Scale), [int]($size.Height * $Scale),
        96 * $Scale, 96 * $Scale, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($surface)
    $bitmap.Freeze()
    $renders[$variant.Name] = $bitmap
    $surfaces[$variant.Name] = $surface
    Write-Png $bitmap (Join-Path $outputDirectory $variant.FileName)
}

foreach ($pair in @(
    @{ Left = 'current'; Right = 'mint'; FileName = 'mint-keyglow-comparison.png'; Focus = $false },
    @{ Left = 'mint-original-center'; Right = 'mint'; FileName = 'mint-keyglow-center-comparison.png'; Focus = $false },
    @{ Left = 'mint-original-center'; Right = 'mint'; FileName = 'mint-keyglow-center-detail.png'; Focus = $true }
)) {
    $gap = 20
    $panelWidth = if ($pair.Focus) { 240 } else { $size.Width }
    $panelHeight = if ($pair.Focus) { 240 } else { $size.Height }
    $comparisonWidth = $panelWidth * 2 + $gap
    $comparison = [System.Windows.Media.DrawingVisual]::new()
    $drawing = $comparison.RenderOpen()
    $drawing.DrawRectangle([System.Windows.Media.Brushes]::White, $null,
        [System.Windows.Rect]::new(0, 0, $comparisonWidth, $panelHeight))

    $index = 0
    foreach ($name in @($pair.Left, $pair.Right)) {
        $bounds = [System.Windows.Rect]::new($index * ($panelWidth + $gap), 0, $panelWidth, $panelHeight)
        if ($pair.Focus) {
            $brush = [System.Windows.Media.VisualBrush]::new($surfaces[$name])
            $brush.ViewboxUnits = [System.Windows.Media.BrushMappingMode]::Absolute
            $brush.Viewbox = [System.Windows.Rect]::new(44, 148, 120, 120)
            $brush.Stretch = [System.Windows.Media.Stretch]::Fill
            $drawing.DrawRectangle($brush, $null, $bounds)
        } else {
            $drawing.DrawImage($renders[$name], $bounds)
        }
        $index++
    }
    $drawing.Close()
    $comparisonBitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(
        [int]($comparisonWidth * $Scale), [int]($panelHeight * $Scale),
        96 * $Scale, 96 * $Scale, [System.Windows.Media.PixelFormats]::Pbgra32)
    $comparisonBitmap.Render($comparison)
    Write-Png $comparisonBitmap (Join-Path $outputDirectory $pair.FileName)
}
