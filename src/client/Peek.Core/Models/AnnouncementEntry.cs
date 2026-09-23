namespace Peek.Core.Models;

/// <summary>
/// One line of AnnouncementHistoryService's history - what was said, when, and (when the
/// announcement came from a UI element, an OCR scan, or a tracked window) which native window
/// it was about. <see cref="SourceWindowHandle"/> is <see cref="nint.Zero"/> for announcements
/// with no such window (settings/navigation speech, a selected-but-not-running installed app,
/// ...) - Announcement History's replay simply skips trying to activate a window for those.
/// </summary>
public sealed record AnnouncementEntry(string Text, DateTimeOffset Timestamp, nint SourceWindowHandle)
{
    /// <summary>
    /// What a screen reader announces when a history row gets keyboard focus - composed once
    /// here, rather than in XAML, so it can't drift from what the row visually shows.
    /// </summary>
    public string AccessibleName => $"{Timestamp:T} — {Text}";
}
