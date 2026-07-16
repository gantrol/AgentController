using System.Collections.ObjectModel;
using System.Globalization;

namespace CodexController.Localization;

public abstract class DictionaryStringCatalog : IStringCatalog
{
    private readonly IReadOnlyDictionary<string, string> _values;

    protected DictionaryStringCatalog(
        AppLanguage language,
        string cultureName,
        IReadOnlyDictionary<string, string> values)
    {
        if (language == AppLanguage.Auto)
        {
            throw new ArgumentException(
                "A concrete catalog cannot use the automatic language.",
                nameof(language));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(cultureName);
        ArgumentNullException.ThrowIfNull(values);

        var missingKeys = StringKeys.All.Except(
            values.Keys,
            StringComparer.Ordinal).ToArray();
        var unexpectedKeys = values.Keys.Except(
            StringKeys.All,
            StringComparer.Ordinal).ToArray();
        if (missingKeys.Length > 0 || unexpectedKeys.Length > 0)
        {
            throw new ArgumentException(
                $"Catalog keys do not match StringKeys. " +
                $"Missing: {string.Join(", ", missingKeys)}. " +
                $"Unexpected: {string.Join(", ", unexpectedKeys)}.",
                nameof(values));
        }

        var blankKey = values.FirstOrDefault(
            pair =>
                string.IsNullOrWhiteSpace(pair.Key) ||
                string.IsNullOrWhiteSpace(pair.Value));
        if (blankKey.Key is not null)
        {
            throw new ArgumentException(
                $"Catalog entry '{blankKey.Key}' is blank.",
                nameof(values));
        }

        Language = language;
        Culture = CultureInfo.GetCultureInfo(cultureName);
        _values = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(
                values,
                StringComparer.Ordinal));
        Keys = _values.Keys.ToArray();
    }

    public AppLanguage Language { get; }

    public CultureInfo Culture { get; }

    public IReadOnlyCollection<string> Keys { get; }

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _values.TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException(
                $"String key '{key}' is not present in the " +
                $"{Culture.Name} catalog.");
    }

    public bool TryGet(string key, out string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            value = string.Empty;
            return false;
        }

        if (_values.TryGetValue(key, out var found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public string Format(string key, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return string.Format(Culture, Get(key), arguments);
    }
}
