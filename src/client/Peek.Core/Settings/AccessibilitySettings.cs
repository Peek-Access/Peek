namespace Peek.Core.Settings;

public sealed class AccessibilitySettings
{
    /// <summary>Whether hovering an element announces it (the ElementTracker pipeline) at all.</summary>
    public bool AnnounceOnHover { get; set; } = true;

    /// <summary>Milliseconds to wait for the mouse to settle before announcing - mirrors ElementTracker's existing Throttle(300ms) default.</summary>
    public int HoverThrottleMs { get; set; } = 300;

    /// <summary>
    /// Announce whatever takes keyboard focus, anywhere on the desktop (see FocusTracker /
    /// FocusAnnouncer). This is the interaction model someone who can't see the screen
    /// actually uses - Tab/arrow through an app and hear each control - as opposed to
    /// <see cref="AnnounceOnHover"/>, which needs the user to aim a mouse at something they
    /// can't see. On by default for that reason.
    /// </summary>
    public bool AnnounceOnFocus { get; set; } = true;

    /// <summary>
    /// Milliseconds to wait for focus to settle before announcing. Focus fires in bursts
    /// while an app builds its UI (a dialog opening can move focus several times in a few
    /// dozen ms); only the last one in a burst is worth speaking. Deliberately shorter than
    /// <see cref="HoverThrottleMs"/> - keyboard navigation should feel immediate, and unlike
    /// a drifting mouse, each focus change is already deliberate.
    /// </summary>
    public int FocusThrottleMs { get; set; } = 120;

    /// <summary>Draw the highlight box around the focused element too, not just hovered ones - useful for sighted-assistant and low-vision use.</summary>
    public bool HighlightFocusedElement { get; set; } = true;

    /// <summary>
    /// Announce whatever takes keyboard focus inside Peek's own windows (see
    /// SelfFocusAnnouncer) - Peek reading its own Settings screen, not just every other
    /// application. On by default so Peek is usable standalone; a user pairing Peek with
    /// another always-on screen reader can turn this off to avoid hearing every control
    /// named twice.
    /// </summary>
    public bool AnnounceOwnInterface { get; set; } = true;

    public void Validate()
    {
        if (HoverThrottleMs < 0) HoverThrottleMs = 0;
        if (FocusThrottleMs < 0) FocusThrottleMs = 0;
    }
}
