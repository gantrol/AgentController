using CodexController.Services;

namespace CodexController.Tests;

public sealed class ComposerCatalogTests
{
    [Theory]
    [InlineData("GPT-5.6-Sol-Max", "5.6 Sol Max")]
    [InlineData("GPT-5.6 Sol Max", "5.6 Sol Max")]
    public void CatalogLabelKeepsSolMaxSelectable(
        string displayName,
        string expected)
    {
        Assert.Equal(
            expected,
            CodexComposerService.ModelLabel(displayName));
    }

    [Fact]
    public void EmptyCatalogDoesNotInventReasoningEfforts()
    {
        var catalog = Catalog([]);

        Assert.Empty(catalog.EffortsForModel(0));
    }

    [Fact]
    public void ModelWithoutEffortsDoesNotReceiveFallbackTiers()
    {
        var catalog = Catalog(
        [
            new ComposerModelOption(
                "account-specific-model",
                "Account Model",
                []),
        ]);

        Assert.Empty(catalog.EffortsForModel(0));
    }

    [Fact]
    public void CatalogPreservesOnlyAccountProvidedEfforts()
    {
        var catalog = Catalog(
        [
            new ComposerModelOption(
                "account-specific-model",
                "Account Model",
                ["Light", "Custom Tier"]),
        ]);

        Assert.Equal(
            ["Light", "Custom Tier"],
            catalog.EffortsForModel(0));
    }

    private static ComposerCatalog Catalog(
        IReadOnlyList<ComposerModelOption> models) =>
        new()
        {
            Models = models,
            InitialModelIndex = 0,
            InitialEffort = string.Empty,
            InitialSpeed = "Standard",
        };
}
