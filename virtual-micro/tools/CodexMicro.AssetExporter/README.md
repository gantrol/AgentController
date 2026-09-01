# Codex Micro keypad asset exporter

This tool renders the real WPF `MicroSurfaceWindow` XAML off-screen. The
result keeps the transparent window margin, WPF gradients, key templates,
lighting, and internal key effects instead of approximating them in a second
drawing format. The live window's 29%-opacity exterior drop shadow is omitted
so every pixel outside the keypad silhouette has zero alpha.

From the repository root:

```powershell
dotnet run --project .\virtual-micro\tools\CodexMicro.AssetExporter\CodexMicro.AssetExporter.csproj -- .\virtual-micro\Assets\KeypadExports
```

The exporter writes six single-color 590 x 610 RGBA PNG files, one real-status
showcase composition, and a palette manifest. The showcase uses Codex's actual
blue/green/white/amber/red/off Agent lighting states and presents the lower-left
gauge as `100% SOL`. Change the `Palettes` or `SixColorAgents` entries in
`Program.cs` to adjust the exports.

SVG is intentionally not generated. WPF control templates, blur effects,
and drop shadows do not have a lossless one-to-one SVG conversion. A separate
simplified vector illustration should be maintained if an SVG deliverable is
needed.
