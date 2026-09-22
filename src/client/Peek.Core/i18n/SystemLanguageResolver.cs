using System.Globalization;

namespace Peek.Core.i18n;

/// <summary>
/// Picks the language a fresh install should default to, based on the Windows "Language
/// &amp; region" setting - <see cref="CultureInfo.CurrentUICulture"/>, which .NET seeds from
/// the OS's per-user preferred-UI-language at process start. This is only ever consulted for
/// <see cref="Settings.LocalizationSettings.UiLanguage"/>'s default value: once a settings.json
/// exists (first run has happened, or the user picked a language in Settings), that persisted
/// value wins and the system culture is never consulted again. Peek's spoken language follows
/// suit for free, since <see cref="Settings.LocalizationSettings.TtsLanguage"/> defaults to
/// null ("follow UiLanguage").
/// </summary>
public static class SystemLanguageResolver
{
    /// <summary>The BCP-47 cultures Peek ships translations for - keep in sync with SettingsViewModel.Languages and the Strings.*.resx files.</summary>
    private static readonly string[] SupportedCultures = ["en-US", "zh-CN", "de-DE"];

    private const string FallbackCulture = "en-US";

    public static string ResolveDefaultUiLanguage()
    {
        try
        {
            return ResolveDefaultUiLanguage(CultureInfo.CurrentUICulture);
        }
        catch
        {
            // CurrentUICulture doesn't actually throw, but a first-run default must never
            // take startup down with it.
            return FallbackCulture;
        }
    }

    /// <summary>Matches by two-letter language only (e.g. any German region resolves to "de-DE") since Peek supports one regional variant per language - falls back to English when the system language isn't one Peek has translations for.</summary>
    public static string ResolveDefaultUiLanguage(CultureInfo systemCulture)
    {
        var systemLanguage = systemCulture.TwoLetterISOLanguageName;
        foreach (var candidate in SupportedCultures)
        {
            if (string.Equals(CultureInfo.GetCultureInfo(candidate).TwoLetterISOLanguageName, systemLanguage, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }
        return FallbackCulture;
    }
}
