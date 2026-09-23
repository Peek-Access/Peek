namespace Peek.Core.Models;

/// <summary>
/// One line of AnnouncementHistoryService's history - what was said, when, and (when the
/// announcement came from a UI element, an OCR scan, or a tracked window) which native window
/// it was about. <see cref="SourceWindowHandle"/> is <see cref="nint.Zero"/> for announcements
/// with no such window (settings/navigation speech, a selected-but-not-running installed app,
/// ...) - Announcement History's replay simply skips trying to activate a window for those.
/// </summary>
/// <remarks>
/// A plain class, deliberately not a record: this is bound as a ListBox's SelectedItem, which
/// resolves by equality - two announcements with identical text recorded in the same instant
/// (e.g. a duplicate ambient chime) would be a record's own definition of "equal", making the
/// second occurrence indistinguishable from the first to the binding and letting Replay act on
/// the wrong one. Each Append call produces exactly one instance that's never compared for
/// value equality anywhere, so reference identity is what's actually wanted here.
/// </remarks>
public sealed class AnnouncementEntry(string text, DateTimeOffset timestamp, nint sourceWindowHandle)
{
    public string Text { get; } = text;
    public DateTimeOffset Timestamp { get; } = timestamp;
    public nint SourceWindowHandle { get; } = sourceWindowHandle;

    /// <summary>
    /// What a screen reader announces when a history row gets keyboard focus - composed once
    /// here, rather than in XAML, so it can't drift from what the row visually shows.
    /// </summary>
    public string AccessibleName => $"{Timestamp:T} — {Text}";
}
