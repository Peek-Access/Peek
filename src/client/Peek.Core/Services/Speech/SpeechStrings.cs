using System.Globalization;
using System.Resources;
using Peek.Core.Settings;

namespace Peek.Core.Services.Speech;

/// <summary>
/// Resolves spoken phrasing in the <em>speech</em> language rather than the UI language.
/// </summary>
/// <remarks>
/// Peek's UI strings go through Irihi.Lingua's generated <c>LanguageManager</c>, which is
/// bound to the current UI culture - correct for the UI, wrong for speech: the TTS language
/// is configured separately (<see cref="LocalizationSettings.TtsLanguage"/>) precisely so a
/// user can read the UI in one language and listen in another. Announcing through the UI
/// culture would hand German scaffolding to an English voice.
/// <para>
/// This reads the same embedded .resx set directly through a <see cref="ResourceManager"/>,
/// which takes an explicit culture per lookup and falls back to the neutral (English)
/// resource when a translation is missing - so an untranslated key degrades to English
/// rather than to a blank announcement.
/// </para>
/// </remarks>
public static class SpeechStrings
{
    // Matches the assembly's embedded resource base name (Peek.Core.i18n.Strings.resources),
    // which the SDK derives from RootNamespace + the i18n folder. The .resx files are listed
    // as AdditionalFiles for the Lingua source generator and picked up as EmbeddedResource by
    // the SDK's default globs - both views of the same files.
    private static readonly ResourceManager Resources =
        new("Peek.Core.i18n.Strings", typeof(SpeechStrings).Assembly);

    /// <summary>
    /// The culture spoken announcements should use: the configured TTS language, falling
    /// back to the UI language, then to English. Never throws on a malformed/unknown culture
    /// name in a hand-edited settings.json - speech failing because of a typo in a settings
    /// file would be a particularly bad failure mode for this app.
    /// </summary>
    public static CultureInfo ResolveCulture(LocalizationSettings localization)
    {
        var name = string.IsNullOrWhiteSpace(localization.TtsLanguage)
            ? localization.UiLanguage
            : localization.TtsLanguage;

        if (string.IsNullOrWhiteSpace(name))
            return CultureInfo.InvariantCulture;

        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    /// <summary>Looks up <paramref name="key"/> in <paramref name="culture"/>, falling back to the key itself if the resource is missing entirely.</summary>
    public static string Get(string key, CultureInfo culture) =>
        Resources.GetString(key, culture) ?? key;

    /// <summary>
    /// Looks up a composite-format string and fills it in. The culture is used for the
    /// lookup <em>and</em> the formatting, so numbers embedded in an announcement follow the
    /// same language they're spoken in.
    /// </summary>
    public static string Format(string key, CultureInfo culture, params object?[] args) =>
        string.Format(culture, Get(key, culture), args);

    /// <summary>
    /// The separator between parts of a composed announcement ("Save, button, disabled").
    /// Localized because it is not punctuation-neutral: a Chinese voice reads a full-width
    /// "，" with the right cadence and an ASCII comma without one.
    /// </summary>
    public static string PartSeparator(CultureInfo culture) => Get("Speech_PartSeparator", culture);

    /// <summary>Joins announcement parts with the culture's own separator, skipping empties.</summary>
    public static string Join(CultureInfo culture, IEnumerable<string?> parts) =>
        string.Join(PartSeparator(culture), parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
