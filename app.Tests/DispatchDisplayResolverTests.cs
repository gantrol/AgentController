using CodexController.Localization;
using CodexController.Presentation.Dispatch;

namespace CodexController.Tests;

public sealed class DispatchDisplayResolverTests
{
    [Theory]
    [InlineData(
        DispatchTurnState.Idle,
        DispatchFollowUpBehavior.Unknown,
        DispatchDisplayKind.Send,
        "Send")]
    [InlineData(
        DispatchTurnState.Running,
        DispatchFollowUpBehavior.Steer,
        DispatchDisplayKind.Steer,
        "Steer current turn")]
    [InlineData(
        DispatchTurnState.Running,
        DispatchFollowUpBehavior.Queue,
        DispatchDisplayKind.Queue,
        "Queue next turn")]
    [InlineData(
        DispatchTurnState.Running,
        DispatchFollowUpBehavior.Unknown,
        DispatchDisplayKind.Default,
        "Default dispatch")]
    [InlineData(
        DispatchTurnState.Unknown,
        DispatchFollowUpBehavior.Steer,
        DispatchDisplayKind.Default,
        "Default dispatch")]
    public void ResolvesEnglishCopyWithoutInferringUnknownState(
        DispatchTurnState turnState,
        DispatchFollowUpBehavior followUpBehavior,
        DispatchDisplayKind expectedKind,
        string expectedLabel)
    {
        var resolver = new DispatchDisplayResolver(
            new LocalizationService(AppLanguage.EnUs).Strings);

        var result = resolver.Resolve(turnState, followUpBehavior);

        Assert.Equal(expectedKind, result.Kind);
        Assert.Equal(expectedLabel, result.Label);
        Assert.False(string.IsNullOrWhiteSpace(result.Description));
    }

    [Theory]
    [InlineData(
        DispatchTurnState.Idle,
        DispatchFollowUpBehavior.Unknown,
        "发送")]
    [InlineData(
        DispatchTurnState.Running,
        DispatchFollowUpBehavior.Steer,
        "加入当前运行")]
    [InlineData(
        DispatchTurnState.Running,
        DispatchFollowUpBehavior.Queue,
        "排到下一轮")]
    [InlineData(
        DispatchTurnState.Unknown,
        DispatchFollowUpBehavior.Unknown,
        "默认提交")]
    public void ResolvesChineseLabels(
        DispatchTurnState turnState,
        DispatchFollowUpBehavior followUpBehavior,
        string expectedLabel)
    {
        var resolver = new DispatchDisplayResolver(
            new LocalizationService(AppLanguage.ZhCn).Strings);

        var result = resolver.Resolve(turnState, followUpBehavior);

        Assert.Equal(expectedLabel, result.Label);
    }

    [Fact]
    public void UnknownCopyStatesThatDetectionIsNotVerified()
    {
        var resolver = new DispatchDisplayResolver(
            new LocalizationService(AppLanguage.EnUs).Strings);

        var result = resolver.Resolve(
            DispatchTurnState.Unknown,
            DispatchFollowUpBehavior.Unknown);

        Assert.Equal(DispatchDisplayKind.Default, result.Kind);
        Assert.Contains(
            "has not verified",
            result.Description,
            StringComparison.OrdinalIgnoreCase);
    }
}
