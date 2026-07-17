using CodexController.Services;

namespace CodexController.Tests;

public sealed class ComposerPowerSelectionPolicyTests
{
    [Fact]
    public void BuildsOfficialCompactSequenceWithoutMaxOrUltra()
    {
        var models = new[]
        {
            Model(
                "gpt-5.6-sol",
                "5.6 Sol",
                "Light",
                "Medium",
                "High",
                "Extra High",
                "Max",
                "Ultra"),
            Model(
                "gpt-5.6-terra",
                "5.6 Terra",
                "Light",
                "Medium",
                "High",
                "Extra High"),
        };

        var result = ComposerPowerSelectionPolicy.Build(models);

        Assert.Equal(
            [
                "5.6 Terra Light",
                "5.6 Sol Light",
                "5.6 Sol Medium",
                "5.6 Sol High",
                "5.6 Sol Extra High",
            ],
            result.Select(item => item.DisplayName));
        Assert.DoesNotContain(
            result,
            item => item.Effort is "Max" or "Ultra");
    }

    [Fact]
    public void UsesCompleteTerraFallbackWhenPrimaryIsTooSmall()
    {
        var result = ComposerPowerSelectionPolicy.Build(
        [
            Model(
                "gpt-5.6-terra",
                "5.6 Terra",
                "Light",
                "Medium",
                "High",
                "Extra High"),
        ]);

        Assert.Equal(4, result.Count);
        Assert.All(
            result,
            item => Assert.Equal(
                "gpt-5.6-terra",
                item.ModelSlug));
    }

    [Fact]
    public void DisablesSimpleModeWhenNeitherSequenceIsViable()
    {
        var result = ComposerPowerSelectionPolicy.Build(
        [
            Model(
                "gpt-5.6-sol",
                "5.6 Sol",
                "Light",
                "Medium"),
        ]);

        Assert.Empty(result);
    }

    [Fact]
    public void FindsOnlyExactCurrentModelAndEffortPair()
    {
        var selections = ComposerPowerSelectionPolicy.Build(
        [
            Model(
                "gpt-5.6-sol",
                "5.6 Sol",
                "Light",
                "Medium",
                "High",
                "Extra High"),
            Model(
                "gpt-5.6-terra",
                "5.6 Terra",
                "Light"),
        ]);

        Assert.Equal(
            2,
            ComposerPowerSelectionPolicy.FindCurrentIndex(
                selections,
                modelIndex: 0,
                effort: "Medium"));
        Assert.Equal(
            -1,
            ComposerPowerSelectionPolicy.FindCurrentIndex(
                selections,
                modelIndex: 0,
                effort: "Max"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void OffGridSolMaxEntersSimpleAtExtraHigh(int direction)
    {
        var selections = ComposerPowerSelectionPolicy.Build(
        [
            Model(
                "gpt-5.6-sol",
                "5.6 Sol",
                "Light",
                "Medium",
                "High",
                "Extra High",
                "Max"),
            Model(
                "gpt-5.6-terra",
                "5.6 Terra",
                "Light"),
        ]);

        var next = ComposerPowerSelectionPolicy.ResolveNextIndex(
            selections,
            modelIndex: 0,
            effort: "Max",
            direction: direction);

        Assert.Equal("5.6 Sol Extra High", selections[next].DisplayName);
    }

    [Theory]
    [InlineData(1, "5.6 Terra Light")]
    [InlineData(-1, "5.6 Sol Extra High")]
    public void UnknownModelEntersSimpleFromDirectionalEdge(
        int direction,
        string expected)
    {
        var selections = ComposerPowerSelectionPolicy.Build(
        [
            Model(
                "gpt-5.6-sol",
                "5.6 Sol",
                "Light",
                "Medium",
                "High",
                "Extra High"),
            Model(
                "gpt-5.6-terra",
                "5.6 Terra",
                "Light"),
        ]);

        var next = ComposerPowerSelectionPolicy.ResolveNextIndex(
            selections,
            modelIndex: 99,
            effort: "Medium",
            direction: direction);

        Assert.Equal(expected, selections[next].DisplayName);
    }

    private static ComposerModelOption Model(
        string slug,
        string name,
        params string[] efforts)
    {
        return new(slug, name, efforts);
    }
}
