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
        Assert.Equal(Color.FromRgb(0x30, 0x4F, 0xFE), appearance.Color);
        Assert.Equal(0.42, appearance.MaximumOpacity, 3);
        Assert.Equal(appearance.MaximumOpacity, appearance.MinimumOpacity);
        Assert.Null(appearance.PulseHalfCycle);
        Assert.Equal("思考中", appearance.StatusName);
        Assert.Equal("solid", appearance.EffectName);
    }

    [Theory]
    [InlineData(4, 0.18, "breath")]
    [InlineData(6, 0.55, "shallow breath")]
    public void BreathingLightingPulsesInsteadOfShowingAStaticPurpleDot(
        int effect,
        double expectedFloor,
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
        Assert.Equal(expectedFloor, appearance.MinimumOpacity, 3);
        Assert.Equal(TimeSpan.FromMilliseconds(880), appearance.PulseHalfCycle);
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
        Assert.Equal(0, appearance.MaximumOpacity);
        Assert.Equal("未分配", appearance.StatusName);
        Assert.Equal("off", appearance.EffectName);
    }
}
