using System.Windows.Media;
using CodexMicro.Desktop.Services;
using CodexMicro.Protocol;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class AgentLightingAppearanceTests
{
    [Fact]
    public void SolidLightingPreservesProtocolColorAndBrightness()
    {
        var appearance = AgentLightingAppearance.From(new SlotLighting(
            2,
            0x304FFE,
            0.42,
            1,
            0,
            false,
            false,
            false));

        Assert.True(appearance.IsActive);
        Assert.False(appearance.IsCurrentSession);
        Assert.False(appearance.UsesWhiteFallback);
        Assert.Equal(Color.FromRgb(0x30, 0x4F, 0xFE), appearance.Color);
        Assert.Equal(0.42, appearance.DisplayOpacity, 3);
        Assert.Equal(0.30, appearance.WideGlowOpacity, 3);
        Assert.Equal(0.14, appearance.OuterGlowOpacity, 3);
        Assert.Equal(0.07, appearance.CapWashOpacity, 3);
        Assert.Equal(0.34, appearance.LightFieldOpacity, 3);
        Assert.Equal(0.42, appearance.WellWashOpacity, 3);
        Assert.Equal("思考中", appearance.StatusName);
        Assert.Equal("solid", appearance.EffectName);
    }

    [Fact]
    public void CurrentSessionSpreadsStatusColorAcrossTheWholeKey()
    {
        var appearance = AgentLightingAppearance.From(
            new SlotLighting(
                4,
                0xFF0033,
                1,
                4,
                0.4,
                false,
                false,
                false),
            isCurrentSession: true);

        Assert.True(appearance.IsActive);
        Assert.True(appearance.IsCurrentSession);
        Assert.False(appearance.UsesWhiteFallback);
        Assert.Equal(Color.FromRgb(0xFF, 0x00, 0x33), appearance.Color);
        Assert.Equal(0.38, appearance.WideGlowOpacity, 3);
        Assert.Equal(0.18, appearance.OuterGlowOpacity, 3);
        Assert.Equal(0.20, appearance.CapWashOpacity, 3);
        Assert.Equal(0.38, appearance.LightFieldOpacity, 3);
        Assert.Equal(0.38, appearance.WellWashOpacity, 3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void CurrentSessionWithoutAStatusUsesWhiteSelectionLight(
        int? color)
    {
        var lighting = color is null
            ? null
            : new SlotLighting(
                3,
                color.Value,
                0,
                0,
                0,
                false,
                false,
                true);

        var appearance = AgentLightingAppearance.From(
            lighting,
            isCurrentSession: true);

        Assert.True(appearance.IsActive);
        Assert.True(appearance.IsCurrentSession);
        Assert.True(appearance.UsesWhiteFallback);
        Assert.Equal(Colors.White, appearance.Color);
        Assert.Equal(1, appearance.DisplayOpacity, 3);
        Assert.Equal(0.90, appearance.WideGlowOpacity, 3);
        Assert.Equal(0.50, appearance.OuterGlowOpacity, 3);
        Assert.Equal("当前会话 · 无对应状态", appearance.StatusName);
        Assert.Equal("white fallback", appearance.EffectName);
    }

    [Fact]
    public void CurrentSessionResolutionPrefersOneBreathingSlotThenRetainsIt()
    {
        var solid = new SlotLighting(
            1,
            0x304FFE,
            1,
            1,
            0,
            false,
            false,
            false);
        var selected = solid with { SlotId = 4, Effect = 4, Speed = 0.4 };

        var resolved = AgentLightingAppearance.ResolveCurrentSessionSlot(
            [solid, selected],
            retainedSlot: 1,
            rosterSlots: [0, 1, 4]);
        Assert.Equal(4, resolved);

        var retained = AgentLightingAppearance.ResolveCurrentSessionSlot(
            [solid],
            retainedSlot: resolved,
            rosterSlots: [0, 1, 4]);
        Assert.Equal(4, retained);
    }

    [Fact]
    public void CurrentSessionResolutionFallsBackToFirstRosterSlot()
    {
        var resolved = AgentLightingAppearance.ResolveCurrentSessionSlot(
            [],
            retainedSlot: null,
            rosterSlots: [2, 5]);

        Assert.Equal(2, resolved);
    }

    [Theory]
    [InlineData(4, "breath")]
    [InlineData(6, "shallow breath")]
    public void BreathingLightingUsesStableVirtualPreview(
        int effect,
        string expectedName)
    {
        var appearance = AgentLightingAppearance.From(new SlotLighting(
            0,
            0x00FF4C,
            1,
            effect,
            0.4,
            false,
            false,
            false));

        Assert.True(appearance.IsActive);
        Assert.Equal(Color.FromRgb(0x00, 0xFF, 0x4C), appearance.Color);
        Assert.Equal(1, appearance.DisplayOpacity, 3);
        Assert.Equal("已完成", appearance.StatusName);
        Assert.Equal(expectedName, appearance.EffectName);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(0xFFFFFF, 0, 1)]
    [InlineData(0xFFFFFF, 1, 0)]
    public void OffLightingIsTransparent(
        int color,
        double brightness,
        int effect)
    {
        var appearance = AgentLightingAppearance.From(new SlotLighting(
            1,
            color,
            brightness,
            effect,
            0,
            false,
            false,
            color == 0));

        Assert.False(appearance.IsActive);
        Assert.Equal(0, appearance.DisplayOpacity);
        Assert.Equal("未分配", appearance.StatusName);
        Assert.Equal("off", appearance.EffectName);
    }
}
