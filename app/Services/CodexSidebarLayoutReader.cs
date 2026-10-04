using System.Text.Json;
using CodexController.Models;

namespace CodexController.Services;

internal static class CodexSidebarLayoutReader
{
    public static CodexSidebarLayout? Read(JsonElement root,
        IReadOnlyDictionary<string, string> projects, string? accountId)
    {
        JsonElement Setting(string name)
        {
            if (root.TryGetProperty(name, out var value)) return value;
            return root.TryGetProperty("electron-persisted-atom-state", out var atoms) &&
                atoms.TryGetProperty(name, out value) ? value : default;
        }

        var accounts = Setting("sidebar-custom-sections-v3");
        var preferences = Setting("flat-project-sidebar-preferences-v1");
        if (accounts.ValueKind != JsonValueKind.Object && preferences.ValueKind != JsonValueKind.Object)
            return null;

        var sections = new List<CodexSidebarSection>();
        JsonElement state = default;
        if (accounts.ValueKind == JsonValueKind.Object && accountId is not null)
            accounts.TryGetProperty(accountId, out state);
        if (state.ValueKind == JsonValueKind.Object && state.TryGetProperty("sections", out var items) &&
            items.ValueKind == JsonValueKind.Array)
        {
            foreach (var section in items.EnumerateArray())
            {
                var id = String(section, "id");
                var name = String(section, "name");
                if (id is null || name is null) continue;
                sections.Add(new("custom:" + id, name, Keys(Property(section, "itemKeys"), projects)));
            }
        }
        var manualVersion = Property(preferences, "manualSortVersion");
        var manualEnabled = manualVersion.ValueKind == JsonValueKind.Number && manualVersion.TryGetInt32(out var version) && version == 1 &&
            Setting("codex-sidebar-sort-mode-v1").ValueKind is JsonValueKind.Undefined or JsonValueKind.Null;
        return new(sections, Strings(Property(state, "sectionOrder")),
            Keys(Setting("unified-sidebar-pinned-order-v1"), projects),
            Keys(Setting("unified-sidebar-project-order-v1"), projects),
            Keys(Setting("unified-sidebar-chat-order-v1"), projects),
            manualEnabled && String(preferences, "chatSortMode") == "manual",
            manualEnabled && String(preferences, "projectSortMode") == "manual");
    }

    private static IReadOnlyList<string> Keys(JsonElement array, IReadOnlyDictionary<string, string> projects) =>
        Strings(array).Select(key =>
            key.StartsWith("codex:thread:local:", StringComparison.Ordinal) && Guid.TryParse(key[19..], out _)
                ? key[19..]
                : key.StartsWith("codex:project:", StringComparison.Ordinal)
                    ? projects.GetValueOrDefault(key[14..]) : null)
            .Where(id => id is not null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private static string? String(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static IReadOnlyList<string> Strings(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : [];
}
