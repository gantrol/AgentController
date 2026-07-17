using CodexController.Services;

namespace CodexController.Tests;

public sealed class ComposerPickerViewPolicyTests
{
    [Theory]
    [InlineData("Power")]
    [InlineData("Show advanced options")]
    public void DetectsSimpleFromLiveMenuItems(string marker)
    {
        Assert.Equal(
            ComposerPickerView.Simple,
            ComposerPickerViewPolicy.Detect([marker]));
    }

    [Fact]
    public void DetectsAdvancedFromLiveMenuItems()
    {
        Assert.Equal(
            ComposerPickerView.Advanced,
            ComposerPickerViewPolicy.Detect(
                ["Model 5.6 Sol", "Effort High", "Speed Standard"]));
    }

    [Fact]
    public void TreatsTransitionStateAsUnknown()
    {
        Assert.Equal(
            ComposerPickerView.Unknown,
            ComposerPickerViewPolicy.Detect(["Advanced"]));
    }

    [Fact]
    public void TreatsBothViewsMountedDuringAnimationAsUnknown()
    {
        Assert.Equal(
            ComposerPickerView.Unknown,
            ComposerPickerViewPolicy.Detect(
                ["Power", "Model 5.6", "Effort High"]));
    }

    [Theory]
    [InlineData("Advanced", ComposerPickerView.Advanced, true)]
    [InlineData("Advanced", ComposerPickerView.Simple, false)]
    [InlineData("Show compact options", ComposerPickerView.Simple, true)]
    [InlineData("Reset to default", ComposerPickerView.Simple, false)]
    public void OnlyUsesNonMutatingToggleForRequestedDirection(
        string name,
        ComposerPickerView desired,
        bool expected)
    {
        Assert.Equal(
            expected,
            ComposerPickerViewPolicy.IsViewToggleToward(name, desired));
    }

    [Theory]
    [InlineData("Enable fast mode", true, false)]
    [InlineData("Enable standard mode", false, true)]
    [InlineData("启用快速模式", true, false)]
    [InlineData("关闭快速模式", false, true)]
    public void ClassifiesLiveSpeedAction(
        string name,
        bool enablesFast,
        bool enablesStandard)
    {
        Assert.Equal(
            enablesFast,
            ComposerPickerViewPolicy.IsEnableFastAction(name));
        Assert.Equal(
            enablesStandard,
            ComposerPickerViewPolicy.IsEnableStandardAction(name));
    }
}
