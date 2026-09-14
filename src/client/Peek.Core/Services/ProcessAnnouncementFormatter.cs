using System.Globalization;
using Peek.Core.Services.Speech;

namespace Peek.Core.Services;

/// <summary>
/// Formats the spoken sentences for the Process Monitor - selection, kill, and open-folder
/// outcomes. Phrasing is resolved in the speech culture (see <see cref="SpeechStrings"/>);
/// process names and OS error reasons are passed through untranslated.
/// </summary>
public static class ProcessAnnouncementFormatter
{
    /// <summary>Brief: "name, process id.". Detailed (ProcessMonitorSettings.AnnounceDetailedInfo): also includes memory usage.</summary>
    public static string FormatSelection(string name, int processId, bool detailed, long memoryBytes, CultureInfo culture)
    {
        var displayName = string.IsNullOrWhiteSpace(name)
            ? SpeechStrings.Get("Speech_Process_Unnamed", culture)
            : name;

        return detailed
            ? SpeechStrings.Format("Speech_Process_WithMemoryFormat", culture, displayName, processId, FormatBytes(memoryBytes, culture))
            : SpeechStrings.Format("Speech_Process_Format", culture, displayName, processId);
    }

    public static string FormatKillSucceeded(string name, CultureInfo culture) =>
        SpeechStrings.Format("Speech_Process_KillSucceededFormat", culture, NameOrFallback(name, culture));

    public static string FormatKillFailed(string name, string? reason, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(reason)
            ? SpeechStrings.Format("Speech_Process_KillFailedFormat", culture, NameOrFallback(name, culture))
            : SpeechStrings.Format("Speech_Process_KillFailedReasonFormat", culture, NameOrFallback(name, culture), reason);

    public static string FormatOpenFolderFailed(string name, string? reason, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(reason)
            ? SpeechStrings.Format("Speech_Process_OpenFolderFailedFormat", culture, NameOrFallback(name, culture))
            : SpeechStrings.Format("Speech_Process_OpenFolderFailedReasonFormat", culture, NameOrFallback(name, culture), reason);

    private static string NameOrFallback(string name, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(name) ? SpeechStrings.Get("Speech_Process_Fallback", culture) : name;

    private static string FormatBytes(long bytes, CultureInfo culture)
    {
        const double mb = 1024 * 1024;
        const double gb = mb * 1024;
        return bytes >= gb
            ? SpeechStrings.Format("Speech_Size_GigabytesFormat", culture, (bytes / gb).ToString("0.0", culture))
            : SpeechStrings.Format("Speech_Size_MegabytesFormat", culture, Math.Max(0, bytes / mb).ToString("0", culture));
    }
}
