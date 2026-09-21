using System.Drawing;

namespace Peek.Core.Abstractions;

public interface IHighlightService
{
    void Initialize();

    /// <summary>
    /// <paramref name="targetHwnd"/> is optional but should be passed whenever the
    /// highlighted rect belongs to a specific window: while set, a background watchdog
    /// auto-hides the highlight the moment that window closes or is minimized - without it,
    /// a highlight shown once and never explicitly cleared would otherwise linger
    /// indefinitely over the desktop after its target disappears.
    /// </summary>
    void Show(Rectangle rect, nint? targetHwnd = null);
    void Hide();
    void Resume();
    void Reset();

    /// <summary>
    /// <paramref name="targetHwnd"/> should be passed by a caller tracking the mouse (unlike
    /// <see cref="Show"/>'s hover-only-at-first-contact use, this is called on every settled
    /// position, so the watchdog's target has to be kept in sync here too - see
    /// HighlightService's remarks on why <see cref="Show"/> alone isn't enough for that).
    /// Omit it only when the caller has no specific window in mind.
    /// </summary>
    void UpdateLocation(Rectangle rect, nint? targetHwnd = null);
    Task UpdateLocationAsync(Rectangle rect, nint? targetHwnd = null);
    void Clear();
    void UpdateColorsRandomly();
    Task StartBreathAsync(CancellationToken cancellationToken);
    Task StopBreathAsync();
}
