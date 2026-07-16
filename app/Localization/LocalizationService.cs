using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace CodexController.Localization;

/// <summary>
/// Owns the current catalog and switches it without changing thread or process
/// culture. The same <see cref="LocalizedStrings"/> instance remains bindable
/// for the lifetime of the service.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    private readonly Func<CultureInfo> _systemCultureProvider;
    private AppLanguage _selectedLanguage;
    private AppLanguage _effectiveLanguage;
    private IStringCatalog _catalog;

    public LocalizationService(
        AppLanguage selectedLanguage = AppLanguage.Auto,
        Func<CultureInfo>? systemCultureProvider = null)
    {
        ValidateLanguage(selectedLanguage);
        _systemCultureProvider =
            systemCultureProvider ??
            (() => CultureInfo.CurrentUICulture);
        _selectedLanguage = selectedLanguage;
        _effectiveLanguage = selectedLanguage.ResolveEffectiveLanguage(
            GetSystemCulture());
        _catalog = CreateCatalog(_effectiveLanguage);
        Strings = new LocalizedStrings(this);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppLanguage SelectedLanguage
    {
        get => _selectedLanguage;
        set => SetLanguage(value);
    }

    public AppLanguage EffectiveLanguage => _effectiveLanguage;

    public string SettingValue =>
        _selectedLanguage.ToSettingValue();

    public CultureInfo Culture => _catalog.Culture;

    public IStringCatalog Catalog => _catalog;

    public LocalizedStrings Strings { get; }

    public IReadOnlyList<LocalizedLanguageOption> LanguageOptions =>
    [
        new(
            AppLanguage.Auto,
            Strings.SettingsLanguageAuto),
        new(
            AppLanguage.ZhCn,
            Strings.SettingsLanguageZhCn),
        new(
            AppLanguage.EnUs,
            Strings.SettingsLanguageEnUs),
    ];

    public void SetLanguage(string? settingValue)
    {
        SetLanguage(AppLanguageParser.Parse(settingValue));
    }

    public void SetLanguage(AppLanguage language)
    {
        ValidateLanguage(language);
        var selectionChanged = _selectedLanguage != language;
        var effectiveLanguage = language.ResolveEffectiveLanguage(
            GetSystemCulture());
        var catalogChanged =
            effectiveLanguage != _effectiveLanguage;

        if (!selectionChanged && !catalogChanged)
        {
            return;
        }

        _selectedLanguage = language;
        if (catalogChanged)
        {
            _effectiveLanguage = effectiveLanguage;
            _catalog = CreateCatalog(effectiveLanguage);
        }

        if (selectionChanged)
        {
            OnPropertyChanged(nameof(SelectedLanguage));
            OnPropertyChanged(nameof(SettingValue));
        }

        if (catalogChanged)
        {
            OnPropertyChanged(nameof(EffectiveLanguage));
            OnPropertyChanged(nameof(Culture));
            OnPropertyChanged(nameof(Catalog));
            OnPropertyChanged(nameof(LanguageOptions));
            Strings.Refresh();
        }
    }

    /// <summary>
    /// Re-evaluates Windows UI culture while the preference is automatic.
    /// Returns true when the effective catalog changed.
    /// </summary>
    public bool RefreshAutoLanguage()
    {
        if (_selectedLanguage != AppLanguage.Auto)
        {
            return false;
        }

        var effectiveLanguage =
            AppLanguage.Auto.ResolveEffectiveLanguage(
                GetSystemCulture());
        if (effectiveLanguage == _effectiveLanguage)
        {
            return false;
        }

        _effectiveLanguage = effectiveLanguage;
        _catalog = CreateCatalog(effectiveLanguage);
        OnPropertyChanged(nameof(EffectiveLanguage));
        OnPropertyChanged(nameof(Culture));
        OnPropertyChanged(nameof(Catalog));
        OnPropertyChanged(nameof(LanguageOptions));
        Strings.Refresh();
        return true;
    }

    private CultureInfo GetSystemCulture()
    {
        return _systemCultureProvider()
            ?? CultureInfo.GetCultureInfo(
                AppLanguageParser.EnUsValue);
    }

    private static IStringCatalog CreateCatalog(
        AppLanguage language)
    {
        return language switch
        {
            AppLanguage.ZhCn => new ZhCatalog(),
            AppLanguage.EnUs => new EnCatalog(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(language),
                language,
                "An effective language must be concrete."),
        };
    }

    private static void ValidateLanguage(AppLanguage language)
    {
        if (!Enum.IsDefined(language))
        {
            throw new ArgumentOutOfRangeException(
                nameof(language),
                language,
                "Unsupported application language.");
        }
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record LocalizedLanguageOption(
    AppLanguage Value,
    string DisplayName)
{
    public string SettingValue => Value.ToSettingValue();
}
