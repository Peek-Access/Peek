using System.Globalization;
using Peek.Core.Services.Speech;

namespace Peek.Core.Services;

public enum WindowAnnouncementKind
{
    Opened,
    Closed,
    FocusChanged,
}

/// <summary>
/// Pure text formatting for window-change announcements, kept separate from
/// WindowAnnouncer's event wiring/IO so the phrasing itself is unit-testable
/// without a real worker/speech service/window hooks. Phrasing is resolved in the
/// <em>speech</em> culture (see <see cref="SpeechStrings"/>), which the caller passes in -
/// the window title itself is whatever the other app called it and is never translated.
/// </summary>
public static class WindowAnnouncementFormatter
{
    /// <summary>Returns null when there's nothing worth announcing (e.g. an empty/whitespace subject).</summary>
    public static string? Format(WindowAnnouncementKind kind, string? subject, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return null;

        var key = kind switch
        {
            WindowAnnouncementKind.Opened => "Speech_Window_Opened",
            WindowAnnouncementKind.Closed => "Speech_Window_Closed",
            WindowAnnouncementKind.FocusChanged => "Speech_Window_FocusChanged",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        return SpeechStrings.Get(key, culture) + SpeechStrings.PartSeparator(culture) + subject;
    }
}
