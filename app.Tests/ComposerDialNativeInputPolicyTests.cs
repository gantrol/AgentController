using CodexController.Services;
using CodexController.Models;
using CodexController.Services.Micro;
using System.Text.Json;

namespace CodexController.Tests;

public sealed class ComposerDialNativeInputPolicyTests
{
    [Theory]
    [InlineData(ComposerDialNavigation.Left, 0x25)]
    [InlineData(ComposerDialNavigation.Up, 0x26)]
    [InlineData(ComposerDialNavigation.Right, 0x27)]
    [InlineData(ComposerDialNavigation.Down, 0x28)]
    public void MapsDialNavigationToNativeArrowKeys(
        ComposerDialNavigation navigation,
        ushort expected)
    {
        Assert.True(
            ComposerDialNativeInputPolicy.TryGetNavigationKey(
                navigation,
                out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void InitialFocusNudgesAwayAndBackToCurrentItem()
    {
        Assert.Equal(
            [0x28, 0x26],
            ComposerDialNativeInputPolicy
                .BuildFocusNudgeSequence(1, 3));
        Assert.Equal(
            [0x26, 0x28],
            ComposerDialNativeInputPolicy
                .BuildFocusNudgeSequence(2, 3));
    }

    [Theory]
    [InlineData(-1, 3)]
    [InlineData(3, 3)]
    [InlineData(0, 0)]
    public void InitialFocusRejectsInvalidNavigationCounts(
        int index,
        int count)
    {
        Assert.Empty(
            ComposerDialNativeInputPolicy
                .BuildFocusNudgeSequence(index, count));
    }

    [Fact]
    public void DialNavigationAlwaysReportsElapsedTime()
    {
        var result = new CodexComposerService().DialNavigate(
            ComposerDialNavigation.Down,
            new AppSettings
            {
                BridgeEnabled = false,
            });

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ElapsedMilliseconds);
        Assert.True(result.ElapsedMilliseconds >= 0);
    }

    [Theory]
    [InlineData("Power")]
    [InlineData("Reasoning effort")]
    [InlineData("推理强度")]
    public void DetectsControlsThatNeedMicroSpecificSemantics(string name)
    {
        Assert.True(
            ComposerDialNativeInputPolicy
                .RequiresMicroSemanticNavigation(name));
    }

    [Theory]
    [InlineData("Model")]
    [InlineData("Full access")]
    [InlineData("Project")]
    public void OrdinaryMenusCanUseNativeNavigation(string name)
    {
        Assert.False(
            ComposerDialNativeInputPolicy
                .RequiresMicroSemanticNavigation(name));
    }

    [Fact]
    public async Task ModelPickerEnterKeepsNativeSessionUntilExplicitDismiss()
    {
        var shortcuts = new List<string>();
        var keys = new List<ushort>();
        var service = new CodexComposerService(
            MicroInputService.Unavailable,
            shortcut =>
            {
                shortcuts.Add(shortcut);
                return true;
            },
            key =>
            {
                keys.Add(key);
                return true;
            });
        var settings = new AppSettings
        {
            BridgeEnabled = true,
            OnlyWhenCodexForeground = false,
            ModelPickerShortcut = "Ctrl+Shift+M",
        };

        var opened = await service.OpenPickerAsync(
            ComposerPickerView.Model,
            settings,
            CancellationToken.None);
        var selected = service.DialSelect(settings);
        var navigated = service.DialNavigate(
            ComposerDialNavigation.Down,
            settings);
        var dismissed = service.DialCancel(settings);

        Assert.True(opened.Succeeded);
        Assert.True(opened.IsMenuOpen);
        Assert.True(selected.Succeeded);
        Assert.True(selected.IsMenuOpen);
        Assert.True(navigated.Succeeded);
        Assert.True(navigated.IsMenuOpen);
        Assert.True(dismissed.Succeeded);
        Assert.False(dismissed.IsMenuOpen);
        Assert.Equal(
            ["Ctrl+Shift+M", "Ctrl+Shift+M"],
            shortcuts);
        Assert.Equal(
            [
                ComposerDialNativeInputPolicy.EnterKey,
                ComposerDialNativeInputPolicy.DownKey,
                ComposerDialNativeInputPolicy.EscapeKey,
            ],
            keys);
    }

    [Fact]
    public async Task OpenMenuKeepsHorizontalArrowsAndVerticalEncoderDetents()
    {
        using var transport = new RecordingTransport();
        using var micro = new MicroInputService(transport);
        var keys = new List<ushort>();
        var service = new CodexComposerService(
            micro,
            _ => true,
            key =>
            {
                keys.Add(key);
                return true;
            });
        var settings = new AppSettings
        {
            BridgeEnabled = true,
            OnlyWhenCodexForeground = false,
            ModelPickerShortcut = "Ctrl+Shift+M",
        };

        var opened = await service.OpenPickerAsync(
            ComposerPickerView.Model,
            settings,
            CancellationToken.None);
        var left = service.DialNavigate(
            ComposerDialNavigation.Left,
            settings);
        var right = service.DialNavigate(
            ComposerDialNavigation.Right,
            settings);
        var up = service.DialNavigate(
            ComposerDialNavigation.Up,
            settings);
        var down = service.DialNavigate(
            ComposerDialNavigation.Down,
            settings);

        Assert.True(opened.Succeeded);
        Assert.All(
            new[] { left, right, up, down },
            result => Assert.True(result.Succeeded));
        Assert.Equal(
            [
                ComposerDialNativeInputPolicy.LeftKey,
                ComposerDialNativeInputPolicy.RightKey,
            ],
            keys);
        Assert.Equal(
            [("ENC_CW", 2), ("ENC_CC", 2)],
            DecodeHidEvents(transport.Reports));
    }

    [Fact]
    public void ClosedHorizontalNavigationNeverBecomesEncoderRotation()
    {
        using var transport = new RecordingTransport();
        using var micro = new MicroInputService(transport);
        var service = new CodexComposerService(
            micro,
            _ => true,
            _ => true);
        var settings = new AppSettings
        {
            BridgeEnabled = true,
            OnlyWhenCodexForeground = false,
        };

        _ = service.DialNavigate(
            ComposerDialNavigation.Left,
            settings);
        _ = service.DialNavigate(
            ComposerDialNavigation.Right,
            settings);

        Assert.Empty(transport.Reports);
    }

    private static IReadOnlyList<(string Key, int Action)> DecodeHidEvents(
        IReadOnlyList<byte[]> reports)
    {
        var events = new List<(string Key, int Action)>();
        foreach (var report in reports)
        {
            using var json = JsonDocument.Parse(
                MicroRpcCodec.DecodePayload(
                    [(ReadOnlyMemory<byte>)report]));
            var parameters = json.RootElement.GetProperty("p");
            events.Add((
                parameters.GetProperty("k").GetString()!,
                parameters.GetProperty("act").GetInt32()));
        }

        return events;
    }

    private sealed class RecordingTransport : IMicroReportTransport
    {
        public List<byte[]> Reports { get; } = [];

        public MicroTransportState State => MicroTransportState.Ready;

        public MicroReportSendResult Send(IReadOnlyList<byte[]> reports)
        {
            Reports.AddRange(reports);
            return MicroReportSendResult.Accepted;
        }

        public void Dispose()
        {
        }
    }
}
