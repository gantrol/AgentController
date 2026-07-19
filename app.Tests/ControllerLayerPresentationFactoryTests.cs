using CodexController.Controllers;
using CodexController.Localization;
using CodexController.Models;
using CodexController.Presentation;

namespace CodexController.Tests;

public sealed class ControllerLayerPresentationFactoryTests
{
    private static readonly ControllerButtons[] RuntimeButtons =
    [
        ControllerButtons.DPadUp,
        ControllerButtons.DPadRight,
        ControllerButtons.DPadDown,
        ControllerButtons.DPadLeft,
        ControllerButtons.Start,
        ControllerButtons.Back,
        ControllerButtons.LeftThumb,
        ControllerButtons.RightThumb,
        ControllerButtons.A,
        ControllerButtons.B,
        ControllerButtons.X,
        ControllerButtons.Y,
    ];

    [Theory]
    [InlineData(AppLanguage.EnUs)]
    [InlineData(AppLanguage.ZhCn)]
    public void TutorialAndOverlayDefinitionsMatchRuntimeInputMap(
        AppLanguage language)
    {
        var command = ControllerLayerPresentationFactory.Command(
            language,
            new ControllerCommandPresentationOptions(
                "Fast",
                "Dictation",
                "Dispatch",
                "Current Codex action",
                "A"));
        var turn = ControllerLayerPresentationFactory.Turn(language);
        var action = ControllerLayerPresentationFactory.Action(
            language,
            new ControllerActionPresentationOptions("A"));

        AssertLayerMatchesRuntime(
            RadialMenuLayerKind.Command,
            command,
            expectedCount: 6);
        AssertLayerMatchesRuntime(
            RadialMenuLayerKind.Turn,
            turn,
            expectedCount: 4);
        AssertLayerMatchesRuntime(
            RadialMenuLayerKind.Action,
            action,
            expectedCount: 6);
    }

    [Fact]
    public void ConfirmationCopyTracksPendingState()
    {
        var command = ControllerLayerPresentationFactory.Command(
            AppLanguage.EnUs,
            new ControllerCommandPresentationOptions(
                "Fast",
                "Dictation",
                "Dispatch",
                "Current Codex action",
                "A",
                IsApproveConfirmationPending: true));
        var action = ControllerLayerPresentationFactory.Action(
            AppLanguage.EnUs,
            new ControllerActionPresentationOptions(
                "A",
                IsClearConfirmationPending: true));

        Assert.Equal(
            "Press A again to confirm",
            command.Single(item => item.Id == "command-approve")
                .Description);
        Assert.Equal(
            "Press A again to confirm",
            action.Single(item => item.Id == "action-clear")
                .Description);
    }

    private static void AssertLayerMatchesRuntime(
        RadialMenuLayerKind layer,
        IEnumerable<ControllerLayerItemPresentation> items,
        int expectedCount)
    {
        var presentation = items.ToArray();
        Assert.Equal(expectedCount, presentation.Length);
        foreach (var item in presentation)
        {
            var action = RadialInputMap.Resolve(
                layer,
                Button(item.Input));
            Assert.NotEqual(RadialInputAction.None, action);
            Assert.Equal(
                item.Id,
                RadialInputMap.ActionId(action, layer));
            Assert.False(string.IsNullOrWhiteSpace(item.Title));
        }

        var actualIds = presentation
            .Select(item => item.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(actualIds.Length, actualIds.Distinct().Count());

        var runtimeIds = RuntimeButtons
            .Select(button => RadialInputMap.Resolve(layer, button))
            .Where(action => action is not (
                RadialInputAction.None or RadialInputAction.Cancel))
            .Select(action => RadialInputMap.ActionId(action, layer))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedCount, runtimeIds.Length);
        Assert.Equal(runtimeIds, actualIds);
    }

    private static ControllerButtons Button(LogicalInput input) =>
        input switch
        {
            LogicalInput.FaceSouth => ControllerButtons.A,
            LogicalInput.FaceEast => ControllerButtons.B,
            LogicalInput.FaceWest => ControllerButtons.X,
            LogicalInput.FaceNorth => ControllerButtons.Y,
            LogicalInput.DPadUp => ControllerButtons.DPadUp,
            LogicalInput.DPadDown => ControllerButtons.DPadDown,
            LogicalInput.DPadLeft => ControllerButtons.DPadLeft,
            LogicalInput.DPadRight => ControllerButtons.DPadRight,
            LogicalInput.View => ControllerButtons.Back,
            LogicalInput.Menu => ControllerButtons.Start,
            _ => throw new ArgumentOutOfRangeException(nameof(input)),
        };
}
