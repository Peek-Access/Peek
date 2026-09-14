namespace Peek.Core.Settings;

/// <summary>
/// Controls optional TTS announcements for window open/close/focus-change events
/// (window-level, not process-level) - entirely off the critical path: WindowAnnouncer
/// checks these live on every event, so toggling any of them takes effect immediately
/// without restarting the underlying OS hooks.
/// </summary>
public sealed class WindowAnnouncementSettings
{
    /// <summary>Master switch. When false, none of the three per-event toggles fire regardless of their own value.</summary>
    public bool Enabled { get; set; } = false;

    public bool AnnounceWindowOpened { get; set; } = true;
    public bool AnnounceWindowClosed { get; set; } = true;
    public bool AnnounceFocusChanged { get; set; } = true;

    public WindowAnnouncementSubject Subject { get; set; } = WindowAnnouncementSubject.WindowTitle;
}

/// <summary>What a window-change announcement says the window "is" - its title or its owning process's name.</summary>
public enum WindowAnnouncementSubject
{
    WindowTitle,
    ProcessName,
}
