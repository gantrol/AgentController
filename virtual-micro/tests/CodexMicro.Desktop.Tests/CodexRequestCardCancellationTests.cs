using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class CodexRequestCardCancellationTests
{
    [Fact]
    public void CurrentRadioCardFixtureIsVerified()
    {
        var result = CodexRequestCardCancellation.Classify([
            RequestCard(radios: 3),
        ]);

        Assert.Equal(CodexRequestCardPresence.Present, result);
    }

    [Fact]
    public void FreeInputCardFixtureIsVerifiedWithoutRadioButtons()
    {
        var result = CodexRequestCardCancellation.Classify([
            RequestCard(radios: 0, edits: 1),
        ]);

        Assert.Equal(CodexRequestCardPresence.Present, result);
    }

    [Fact]
    public void MultiQuestionPaginationStillUsesOneVerifiedRoot()
    {
        var result = CodexRequestCardCancellation.Classify([
            RequestCard(radios: 4, edits: 1),
        ]);

        Assert.Equal(CodexRequestCardPresence.Present, result);
    }

    [Fact]
    public void CancellationTargetMustBeUnique()
    {
        var result = CodexRequestCardCancellation.Classify([
            RequestCard(radios: 3),
            RequestCard(radios: 2),
        ]);

        Assert.Equal(CodexRequestCardPresence.Blocked, result);
    }

    [Fact]
    public void DisappearedCardFallsBackToAgentZero()
    {
        Assert.Equal(
            CodexRequestCardPresence.NotPresent,
            CodexRequestCardCancellation.Classify([]));
    }

    [Fact]
    public void CssClassChangeIsBlockedInsteadOfFallingBackToAgentZero()
    {
        var result = CodexRequestCardCancellation.Classify([
            RequestCard(radios: 3) with { HasRequestCardClass = false },
        ]);

        Assert.Equal(CodexRequestCardPresence.Blocked, result);
    }

    [Fact]
    public void StaleOrIncompleteMarkedCardIsBlocked()
    {
        var result = CodexRequestCardCancellation.Classify([
            RequestCard(radios: 0, edits: 0) with
            {
                DismissButtonCount = 0,
            },
        ]);

        Assert.Equal(CodexRequestCardPresence.Blocked, result);
    }

    private static CodexRequestCardSnapshot RequestCard(
        int radios,
        int edits = 0) => new(
        IsVisible: true,
        HasRequestCardClass: true,
        DismissButtonCount: 1,
        SkipButtonCount: 1,
        RadioButtonCount: radios,
        EditCount: edits);
}
