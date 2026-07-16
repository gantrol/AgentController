using System.Globalization;

namespace CodexController.Localization;

/// <summary>
/// Language preference persisted by the application. <see cref="Auto"/>
/// follows the Windows UI culture without changing the process culture.
/// </summary>
public enum AppLanguage
{
    Auto,
    ZhCn,
    EnUs,
}

public static class AppLanguageParser
{
    public const string AutoValue = "auto";
    public const string ZhCnValue = "zh-CN";
    public const string EnUsValue = "en-US";

    public static AppLanguage Parse(string? value)
    {
        return TryParse(value, out var language)
            ? language
            : AppLanguage.Auto;
    }

    public static bool TryParse(
        string? value,
        out AppLanguage language)
    {
        var normalized = value?
            .Trim()
            .Replace('_', '-')
            .ToLowerInvariant();

        language = normalized switch
        {
            null or "" or "auto" or "system" =>
                AppLanguage.Auto,
            "zh" or "zh-cn" or "zh-hans" or "zh-hans-cn" or
            "zhcn" =>
                AppLanguage.ZhCn,
            "en" or "en-us" or "enus" =>
                AppLanguage.EnUs,
            _ => AppLanguage.Auto,
        };

        return normalized is null or "" or
            "auto" or "system" or
            "zh" or "zh-cn" or "zh-hans" or "zh-hans-cn" or
            "zhcn" or
            "en" or "en-us" or "enus";
    }

    public static string ToSettingValue(this AppLanguage language)
    {
        return language switch
        {
            AppLanguage.Auto => AutoValue,
            AppLanguage.ZhCn => ZhCnValue,
            AppLanguage.EnUs => EnUsValue,
            _ => throw new ArgumentOutOfRangeException(
                nameof(language),
                language,
                "Unsupported application language."),
        };
    }

    public static AppLanguage ResolveEffectiveLanguage(
        this AppLanguage language,
        CultureInfo? systemCulture = null)
    {
        if (language is AppLanguage.ZhCn or AppLanguage.EnUs)
        {
            return language;
        }

        if (language != AppLanguage.Auto)
        {
            throw new ArgumentOutOfRangeException(
                nameof(language),
                language,
                "Unsupported application language.");
        }

        var culture = systemCulture ?? CultureInfo.CurrentUICulture;
        return culture.TwoLetterISOLanguageName.Equals(
            "zh",
            StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.ZhCn
                : AppLanguage.EnUs;
    }
}
