using System.ComponentModel;
using System.Globalization;

namespace CodexController.Localization;

/// <summary>
/// Stable application-wide localization entry point for WPF bindings.
/// Configure it once before constructing the first Window. Replacing the
/// service later is also supported and refreshes existing bindings.
/// </summary>
public static class LocalizationHost
{
    private static readonly object Gate = new();
    private static LocalizationService _current = new();

    public static LocalizationBindingSource Strings { get; } =
        new(_current);

    public static LocalizationService Current
    {
        get
        {
            lock (Gate)
            {
                return _current;
            }
        }
    }

    public static void Use(LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);

        lock (Gate)
        {
            if (ReferenceEquals(_current, localization))
            {
                return;
            }

            _current = localization;
            Strings.Attach(localization);
        }
    }
}

/// <summary>
/// Indexer source used by <see cref="LocExtension"/>. It stays alive when the
/// active service changes, so existing Window and UserControl bindings do not
/// need a new DataContext.
/// </summary>
public sealed class LocalizationBindingSource :
    INotifyPropertyChanged
{
    private LocalizationService _localization;

    internal LocalizationBindingSource(
        LocalizationService localization)
    {
        _localization = localization;
        _localization.Strings.PropertyChanged +=
            OnLocalizedStringsChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppLanguage Language =>
        _localization.EffectiveLanguage;

    public CultureInfo Culture => _localization.Culture;

    public string this[string key] =>
        _localization.Strings[key];

    public string Get(string key) =>
        _localization.Strings.Get(key);

    public string Format(
        string key,
        params object?[] arguments) =>
        _localization.Strings.Format(key, arguments);

    internal void Attach(LocalizationService localization)
    {
        if (ReferenceEquals(_localization, localization))
        {
            return;
        }

        _localization.Strings.PropertyChanged -=
            OnLocalizedStringsChanged;
        _localization = localization;
        _localization.Strings.PropertyChanged +=
            OnLocalizedStringsChanged;
        NotifyCatalogChanged();
    }

    private void OnLocalizedStringsChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        NotifyCatalogChanged();
    }

    private void NotifyCatalogChanged()
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(Language)));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(Culture)));
    }
}
