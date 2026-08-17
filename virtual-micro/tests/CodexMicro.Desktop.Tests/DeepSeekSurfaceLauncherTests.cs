using CodexMicro.Desktop.Services;
using System.Diagnostics;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class DeepSeekSurfaceLauncherTests
{
    [Fact]
    public void BuildsAnAppModeUrlFromTheConfiguredControlEndpoint()
    {
        var harness = new MicroHarnessDefinition(
            "deepseek-harness",
            "DeepSeek Harness",
            "Test Harness",
            null,
            true,
            new(
                null,
                null,
                null,
                null,
                false,
                ControlUri:
                    "http://127.0.0.1:3097/__agentcontroller/micro/request"));

        var surfaceUri = DeepSeekSurfaceLauncher.BuildSurfaceUri(harness);
        var startInfo = DeepSeekSurfaceLauncher.CreateStartInfo(
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            surfaceUri);

        Assert.Equal(
            "http://127.0.0.1:3097/?codexMicroSurface=1",
            surfaceUri.AbsoluteUri);
        Assert.False(startInfo.UseShellExecute);
        Assert.False(startInfo.CreateNoWindow);
        Assert.Equal(ProcessWindowStyle.Normal, startInfo.WindowStyle);
        Assert.Contains(
            "--app=http://127.0.0.1:3097/?codexMicroSurface=1",
            startInfo.ArgumentList);
    }
}
