using CodexController.Services;

namespace CodexController.Tests;

public sealed class ComposerSpeedSelectionPolicyTests
{
    [Theory]
    [InlineData("Fast", true)]
    [InlineData("Fast 1.5x speed, more usage", true)]
    [InlineData("Standard", false)]
    [InlineData("Standard Default speed", false)]
    public void MatchesLiveSpeedOptionLabels(
        string name,
        bool fast)
    {
        Assert.True(
            ComposerSpeedSelectionPolicy.MatchesOption(name, fast));
        Assert.False(
            ComposerSpeedSelectionPolicy.MatchesOption(name, !fast));
    }

    [Theory]
    [InlineData("Speed Fast", true)]
    [InlineData("Speed Standard", false)]
    public void ReadsSelectedSpeedFromAdvancedCategory(
        string categoryName,
        bool fast)
    {
        Assert.True(
            ComposerSpeedSelectionPolicy.MatchesCategory(
                categoryName,
                fast));
        Assert.False(
            ComposerSpeedSelectionPolicy.MatchesCategory(
                categoryName,
                !fast));
    }

    [Theory]
    [InlineData("Fast 1.5x speed, more usage", "Fast")]
    [InlineData("Standard Default speed", "Standard")]
    [InlineData("High", "High")]
    public void DescribedOptionMatchesCompactReadback(
        string optionName,
        string currentValue)
    {
        Assert.True(
            ComposerSpeedSelectionPolicy.OptionMatchesCurrentValue(
                optionName,
                currentValue));
    }

    [Theory]
    [InlineData("Fastest", true)]
    [InlineData("Standardized", false)]
    [InlineData("Speed Fastest", true)]
    public void DoesNotAcceptLongerWordWithSamePrefix(
        string name,
        bool fast)
    {
        var result = name.StartsWith("Speed ", StringComparison.Ordinal)
            ? ComposerSpeedSelectionPolicy.MatchesCategory(name, fast)
            : ComposerSpeedSelectionPolicy.MatchesOption(name, fast);

        Assert.False(result);
    }
}
