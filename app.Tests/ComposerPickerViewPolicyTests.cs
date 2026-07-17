using CodexController.Services;

namespace CodexController.Tests;

public sealed class ComposerPickerViewPolicyTests
{
    [Theory]
    [InlineData("Power")]
    [InlineData("Power 7")]
    [InlineData("Power: Max")]
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
    public void DetectsAdvancedFromSimpleBackToggle()
    {
        Assert.Equal(
            ComposerPickerView.Advanced,
            ComposerPickerViewPolicy.Detect(["Simple"]));
    }

    [Fact]
    public void DetectsScreenshotSimpleViewFromAdvancedToggle()
    {
        Assert.Equal(
            ComposerPickerView.Simple,
            ComposerPickerViewPolicy.Detect(["Advanced"]));
    }

    [Fact]
    public void DetectsSimpleViewFromUnnamedPowerRange()
    {
        Assert.Equal(
            ComposerPickerView.Simple,
            ComposerPickerViewPolicy.Detect(
                [],
                hasPowerRange: true));
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
    [InlineData("Simple", ComposerPickerView.Simple, true)]
    [InlineData("Basic", ComposerPickerView.Simple, true)]
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

    [Theory]
    [InlineData("Turn Fast mode on or off.")]
    [InlineData("Toggle Fast mode")]
    [InlineData("speed-mode-toggle | Fast mode")]
    [InlineData("切换快速模式")]
    public void RecognizesFastLightningToggle(string descriptor)
    {
        Assert.True(
            ComposerPickerViewPolicy.IsFastToggle(descriptor));
    }

    [Fact]
    public void NeutralFastToggleIsNotMistakenForOneWayAction()
    {
        const string descriptor = "Turn Fast mode on or off.";

        Assert.False(
            ComposerPickerViewPolicy.IsEnableFastAction(descriptor));
        Assert.False(
            ComposerPickerViewPolicy.IsEnableStandardAction(descriptor));
    }
}
