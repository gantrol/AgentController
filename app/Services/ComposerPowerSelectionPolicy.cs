namespace CodexController.Services;

internal sealed record ComposerPowerSelection(
    int ModelIndex,
    string ModelSlug,
    string ModelName,
    string Effort,
    int PowerIndex)
{
    public string DisplayName => $"{ModelName} {Effort}";
}

internal static class ComposerPowerSelectionPolicy
{
    private static readonly (string Model, string Effort)[] Primary =
    [
        ("gpt-5.6-terra", "Light"),
        ("gpt-5.6-sol", "Light"),
        ("gpt-5.6-sol", "Medium"),
        ("gpt-5.6-sol", "High"),
        ("gpt-5.6-sol", "Extra High"),
    ];

    private static readonly (string Model, string Effort)[] TerraFallback =
    [
        ("gpt-5.6-terra", "Light"),
        ("gpt-5.6-terra", "Medium"),
        ("gpt-5.6-terra", "High"),
        ("gpt-5.6-terra", "Extra High"),
    ];

    internal static IReadOnlyList<ComposerPowerSelection> Build(
        IReadOnlyList<ComposerModelOption> models)
    {
        ArgumentNullException.ThrowIfNull(models);

        var primary = Resolve(models, Primary);
        if (primary.Count >= 4)
        {
            return primary;
        }

        var fallback = Resolve(models, TerraFallback);
        return fallback.Count == TerraFallback.Length
            ? fallback
            : [];
    }

    internal static int FindCurrentIndex(
        IReadOnlyList<ComposerPowerSelection> selections,
        int modelIndex,
        string? effort)
    {
        ArgumentNullException.ThrowIfNull(selections);
        for (var index = 0; index < selections.Count; index++)
        {
            var candidate = selections[index];
            if (
                candidate.ModelIndex == modelIndex &&
                string.Equals(
                    candidate.Effort,
                    effort,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    internal static int ResolveNextIndex(
        IReadOnlyList<ComposerPowerSelection> selections,
        int modelIndex,
        string? effort,
        int direction)
    {
        ArgumentNullException.ThrowIfNull(selections);
        if (selections.Count == 0 || direction == 0)
        {
            return -1;
        }

        var currentIndex = FindCurrentIndex(
            selections,
            modelIndex,
            effort);
        if (currentIndex >= 0)
        {
            return Math.Clamp(
                currentIndex + Math.Sign(direction),
                0,
                selections.Count - 1);
        }

        var currentEffortRank = EffortRank(effort);
        var sameModel = selections
            .Select((selection, index) => (selection, index))
            .Where(candidate =>
                candidate.selection.ModelIndex == modelIndex)
            .ToArray();
        if (sameModel.Length == 0)
        {
            return direction > 0 ? 0 : selections.Count - 1;
        }

        if (currentEffortRank >= 0)
        {
            var ranked = sameModel
                .Select(candidate => (
                    candidate.index,
                    rank: EffortRank(candidate.selection.Effort)))
                .Where(candidate => candidate.rank >= 0)
                .ToArray();
            var adjacent = direction > 0
                ? ranked
                    .Where(candidate =>
                        candidate.rank > currentEffortRank)
                    .OrderBy(candidate => candidate.rank)
                    .FirstOrDefault()
                : ranked
                    .Where(candidate =>
                        candidate.rank < currentEffortRank)
                    .OrderByDescending(candidate => candidate.rank)
                    .FirstOrDefault();
            if (adjacent != default)
            {
                return adjacent.index;
            }

            return direction > 0
                ? ranked.MaxBy(candidate => candidate.rank).index
                : ranked.MinBy(candidate => candidate.rank).index;
        }

        return direction > 0
            ? sameModel[0].index
            : sameModel[^1].index;
    }

    private static int EffortRank(string? effort)
    {
        string[] values =
            ["Light", "Medium", "High", "Extra High", "Max", "Ultra"];
        for (var index = 0; index < values.Length; index++)
        {
            if (string.Equals(
                    values[index],
                    effort,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static IReadOnlyList<ComposerPowerSelection> Resolve(
        IReadOnlyList<ComposerModelOption> models,
        IReadOnlyList<(string Model, string Effort)> specifications)
    {
        var result = new List<ComposerPowerSelection>();
        for (var powerIndex = 0;
             powerIndex < specifications.Count;
             powerIndex++)
        {
            var specification = specifications[powerIndex];
            var modelIndex = FindModel(models, specification.Model);
            if (modelIndex < 0)
            {
                continue;
            }

            var model = models[modelIndex];
            if (!model.Efforts.Any(effort =>
                    string.Equals(
                        effort,
                        specification.Effort,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            result.Add(new(
                modelIndex,
                model.Slug,
                model.DisplayName,
                specification.Effort,
                powerIndex));
        }

        return result;
    }

    private static int FindModel(
        IReadOnlyList<ComposerModelOption> models,
        string slug)
    {
        for (var index = 0; index < models.Count; index++)
        {
            if (string.Equals(
                    models[index].Slug,
                    slug,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}
