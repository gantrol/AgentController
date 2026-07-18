using CodexController.Services;

namespace CodexController.Tests;

public sealed class ComposerCatalogTests
{
    [Fact]
    public void LoaderFiltersOrdersAndResolvesCurrentSelection()
    {
        var catalogRoot = Path.Combine(
            Path.GetTempPath(),
            $"agent-controller-catalog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(catalogRoot);
        try
        {
            File.WriteAllText(
                Path.Combine(catalogRoot, "models_cache.json"),
                """
                {
                  "models": [
                    {
                      "visibility": "list",
                      "slug": "model-b",
                      "display_name": "GPT-Model-B",
                      "priority": 20,
                      "supported_reasoning_levels": [
                        { "effort": "medium" }
                      ]
                    },
                    {
                      "visibility": "hidden",
                      "slug": "hidden",
                      "display_name": "GPT-Hidden",
                      "priority": 0
                    },
                    {
                      "visibility": "list",
                      "slug": "model-a",
                      "display_name": "GPT-Model-A",
                      "priority": 10,
                      "supported_reasoning_levels": [
                        { "effort": "low" },
                        { "effort": "xhigh" }
                      ]
                    }
                  ]
                }
                """);
            File.WriteAllText(
                Path.Combine(catalogRoot, "config.toml"),
                "model = 'model-b'\n" +
                "model_reasoning_effort = 'medium'\n" +
                "service_tier = 'priority'\n");
            var loader = new CodexComposerCatalogService(
                () => "Model A Extra High",
                () => catalogRoot);

            var catalog = loader.LoadCatalog();

            Assert.Equal(2, catalog.Models.Count);
            Assert.Equal("model-a", catalog.Models[0].Slug);
            Assert.Equal("model-b", catalog.Models[1].Slug);
            Assert.Equal(0, catalog.InitialModelIndex);
            Assert.Equal("Extra High", catalog.InitialEffort);
            Assert.Equal("Fast", catalog.InitialSpeed);
        }
        finally
        {
            Directory.Delete(catalogRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("GPT-5.6-Sol-Max", "5.6 Sol Max")]
    [InlineData("GPT-5.6 Sol Max", "5.6 Sol Max")]
    public void CatalogLabelKeepsSolMaxSelectable(
        string displayName,
        string expected)
    {
        Assert.Equal(
            expected,
            CodexComposerCatalogService.ModelLabel(displayName));
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
