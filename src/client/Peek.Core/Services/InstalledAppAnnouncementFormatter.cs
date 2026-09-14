using System.Globalization;
using Peek.Core.Services.Speech;

namespace Peek.Core.Services;

/// <summary>
/// Formats the spoken sentence for a selected row in the App Monitor - name, version,
/// publisher. Phrasing is resolved in the speech culture (see <see cref="SpeechStrings"/>);
/// app names, version numbers and publisher names are passed through untranslated.
/// </summary>
public static class InstalledAppAnnouncementFormatter
{
    public static string FormatSelection(string name, string version, string publisher, CultureInfo culture)
    {
        var displayName = string.IsNullOrWhiteSpace(name)
            ? SpeechStrings.Get("Speech_App_Unnamed", culture)
            : name;
        var parts = new List<string> { displayName };

        if (!string.IsNullOrWhiteSpace(version))
            parts.Add(SpeechStrings.Format("Speech_App_VersionFormat", culture, version));
        if (!string.IsNullOrWhiteSpace(publisher))
            parts.Add(SpeechStrings.Format("Speech_App_PublisherFormat", culture, publisher));

        return SpeechStrings.Join(culture, parts) + ".";
    }

    public static string FormatLaunchSucceeded(string name, CultureInfo culture) =>
        SpeechStrings.Format("Speech_App_LaunchSucceededFormat", culture, NameOrFallback(name, culture));

    public static string FormatLaunchFailed(string name, string? reason, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(reason)
            ? SpeechStrings.Format("Speech_App_LaunchFailedFormat", culture, NameOrFallback(name, culture))
            : SpeechStrings.Format("Speech_App_LaunchFailedReasonFormat", culture, NameOrFallback(name, culture), reason);

    private static string NameOrFallback(string name, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(name) ? SpeechStrings.Get("Speech_App_Fallback", culture) : name;
}
