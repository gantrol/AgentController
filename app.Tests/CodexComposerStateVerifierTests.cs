using CodexController.Services;

namespace CodexController.Tests;

public sealed class CodexComposerStateVerifierTests
{
    [Theory]
    [InlineData("", true)]
    [InlineData(" \u200B\uFEFF\uFFFC ", true)]
    [InlineData("draft", false)]
    [InlineData(null, false)]
    public void EffectiveEmptyTextIgnoresEditorSentinels(
        string? text,
        bool expected)
    {
        Assert.Equal(
            expected,
            CodexComposerStateVerifier.IsComposerTextEffectivelyEmpty(
                text));
    }

    [Fact]
    public void DraftComparisonNormalizesLineEndingsOnly()
    {
        Assert.True(CodexComposerStateVerifier.ComposerDraftEquals(
            "line 1\r\nline 2",
            "line 1\nline 2"));
        Assert.False(CodexComposerStateVerifier.ComposerDraftEquals(
            "line 1 ",
            "line 1"));
    }

    [Theory]
    [InlineData("/pdraft", "draft", true)]
    [InlineData("/draft", "draft", true)]
    [InlineData("draft", "draft", false)]
    public void PlanProbeDetectionRecognizesOnlyInjectedPrefix(
        string actual,
        string original,
        bool expected)
    {
        Assert.Equal(
            expected,
            CodexComposerStateVerifier.HasInjectedPlanQuery(
                actual,
                original));
    }
}
