using CodexController.Services;

namespace CodexController.Tests;

public sealed class CurrentControlActionPolicyTests
{
    [Theory]
    [InlineData(ComposerDialNavigation.Left)]
    [InlineData(ComposerDialNavigation.Right)]
    public void AdjustableComposerControlKeepsLiteralDirection(
        ComposerDialNavigation navigation)
    {
        var expected = navigation == ComposerDialNavigation.Left
            ? CurrentControlAction.NativeLeft
            : CurrentControlAction.NativeRight;

        Assert.Equal(
            expected,
            CurrentControlActionPolicy.Resolve(
                Readback(
                    CodexMicroSurfaceKind.Composer,
                    adjustable: true),
                navigation));
    }

    [Fact]
    public void RightEntersVerifiedExpandableControlThroughEncoderPress()
    {
        Assert.Equal(
            CurrentControlAction.EncoderPress,
            CurrentControlActionPolicy.Resolve(
                Readback(
                    CodexMicroSurfaceKind.Composer,
                    canExpand: true),
                ComposerDialNavigation.Right));
    }

    [Fact]
    public void LeftClosesMenuEvenWhenNoItemHasHighlight()
    {
        Assert.Equal(
            CurrentControlAction.Escape,
            CurrentControlActionPolicy.Resolve(
                Readback(
                    CodexMicroSurfaceKind.Menu,
                    verified: false),
                ComposerDialNavigation.Left));
    }

    [Theory]
    [InlineData(ComposerDialNavigation.Left)]
    [InlineData(ComposerDialNavigation.Right)]
    public void UnverifiedClosedControlNeverInjectsInput(
        ComposerDialNavigation navigation)
    {
        Assert.Equal(
            CurrentControlAction.None,
            CurrentControlActionPolicy.Resolve(
                Readback(
                    CodexMicroSurfaceKind.Composer,
                    verified: false),
                navigation));
    }

    [Fact]
    public void RightDoesNotConfirmMenuLeaf()
    {
        Assert.Equal(
            CurrentControlAction.None,
            CurrentControlActionPolicy.Resolve(
                Readback(CodexMicroSurfaceKind.Menu),
                ComposerDialNavigation.Right));
    }

    private static CodexMicroReadback Readback(
        CodexMicroSurfaceKind surface,
        bool verified = true,
        bool canExpand = false,
        bool adjustable = false) =>
        new(
            surface,
            surface.ToString(),
            "Target",
            1,
            3,
            verified,
            canExpand,
            adjustable);
}
