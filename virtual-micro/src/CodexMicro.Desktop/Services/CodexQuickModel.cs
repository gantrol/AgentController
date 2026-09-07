namespace CodexMicro.Desktop.Services;

internal readonly record struct CodexQuickModel
{
    private readonly string? _id;

    private CodexQuickModel(string id) => _id = id;

    internal string Id => _id ?? string.Empty;
    internal static CodexQuickModel Unknown => default;
    internal static CodexQuickModel Sol => new("gpt-5.6-sol");
    internal static CodexQuickModel Terra => new("gpt-5.6-terra");
    internal static CodexQuickModel Luna => new("gpt-5.6-luna");

    internal static CodexQuickModel FromId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? default : new(value.Trim());

    internal static CodexQuickModel FromSetting(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "sol" => Sol,
            "terra" => Terra,
            "luna" => Luna,
            _ => FromId(value),
        };

    public override string ToString() => Id;
}
