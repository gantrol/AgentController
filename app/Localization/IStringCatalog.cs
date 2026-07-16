using System.Globalization;

namespace CodexController.Localization;

public interface IStringCatalog
{
    AppLanguage Language { get; }

    CultureInfo Culture { get; }

    IReadOnlyCollection<string> Keys { get; }

    string this[string key] { get; }

    string Get(string key);

    bool TryGet(string key, out string value);

    string Format(string key, params object?[] arguments);
}
