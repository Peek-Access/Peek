using System.Drawing;

namespace Peek.Core.Abstractions;

public interface IHighlightService
{
    void Initialize();

    /// <summary>
    /// <paramref name="targetHwnd"/> is optional but should be passed whenever the
    /// highlighted rect belongs to a specific window: while set, a background watchdog
    /// auto-hides the highlight the moment that window closes or is minimized, or Peek's
    /// own window is minimized/hidden - without it, a highlight shown once and never
    /// explicitly cleared would otherwise linger indefinitely over the desktop after its
    /// target disappears.
    /// </summary>
    void Show(Rectangle rect, nint? targetHwnd = null);
    void Hide();
    void Resume();
    void Reset();
    void UpdateLocation(Rectangle rect);
    Task UpdateLocationAsync(Rectangle rect);
    void Clear();
    void UpdateColorsRandomly();
    Task StartBreathAsync(CancellationToken cancellationToken);
    Task StopBreathAsync();
}
