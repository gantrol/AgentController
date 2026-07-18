using CodexMicro.Desktop.Services;
using CodexMicro.Desktop.Controls;
using Xunit;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexMicro.Desktop.Tests;

public sealed class CodexMicroLayoutObserverTests
{
    [Fact]
    public void OfficialCodexVectorGeometryInitializesWithoutRasterFallback()
    {
        Exception? error = null;
        string? keycapId = null;
        var thread = new Thread(() =>
        {
            try
            {
                var icon = new KeycapIcon { KeycapId = "CODEX" };
                keycapId = icon.KeycapId;
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
        Assert.Equal("CODEX", keycapId);
    }

    [Fact]
    public void EveryCodexKeycapCatalogEntryRendersOffscreen()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var keycapId in CodexKeycapCatalog.KnownIds)
                {
                    var icon = new KeycapIcon
                    {
                        KeycapId = keycapId,
                        Width = 40,
                        Height = 40,
                    };
                    icon.Measure(new Size(40, 40));
                    icon.Arrange(new Rect(0, 0, 40, 40));
                    var bitmap = new RenderTargetBitmap(
                        40,
                        40,
                        96,
                        96,
                        PixelFormats.Pbgra32);
                    bitmap.Render(icon);
                }
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }

    [Fact]
    public void ParseReadsExpandedCodexDesktopTables()
    {
        const string toml = """
            [desktop.codex-micro-layout]
            version = 1
            encoderMode = "reasoning"

            [desktop.codex-micro-layout.slots.ACT06]
            keycapId = "BUG"
            commandId = "feedback"

            [desktop.codex-micro-layout.slots.ACT12]
            keycapId = "OAI"

            [desktop.codex-micro-layout.analogStick.up]
            type = "command"
            commandId = "newTask"
            """;

        var result = CodexMicroLayoutObserver.Parse(toml, "test");

        Assert.Equal("reasoning", result.EncoderMode);
        Assert.Equal("BUG", result.GetSlot("ACT06").KeycapId);
        Assert.Equal("feedback", result.GetSlot("ACT06").CommandId);
        Assert.Equal("OAI", result.GetSlot("ACT12").KeycapId);
        Assert.Equal("newTask", result.AnalogActions["up"]);
        Assert.Equal("APPR", result.GetSlot("ACT07").KeycapId);
    }

    [Fact]
    public void ParseReadsCompactInlineLayout()
    {
        const string toml = """
            [desktop]
            codex-micro-layout = { version = 1, slots = { ACT07 = { keycapId = "TERM", commandId = "toggleTerminal" }, ACT10_ACT11 = { keycapId = "PAINT" } }, encoderMode = "reasoning" }
            """;

        var result = CodexMicroLayoutObserver.Parse(toml, "test");

        Assert.Equal("TERM", result.GetSlot("ACT07").KeycapId);
        Assert.Equal("toggleTerminal", result.GetSlot("ACT07").CommandId);
        Assert.Equal("PAINT", result.GetSlot("ACT10_ACT11").KeycapId);
        Assert.Equal("reasoning", result.EncoderMode);
    }

    [Fact]
    public void ParseRejectsUnknownKeycapAndRetainsDefault()
    {
        const string toml = """
            [desktop.codex-micro-layout.slots.ACT08]
            keycapId = "NOT_A_CODEX_KEYCAP"
            """;

        var result = CodexMicroLayoutObserver.Parse(toml, "test");

        Assert.Equal("REJ", result.GetSlot("ACT08").KeycapId);
    }
}
