using Peek.Core.Abstractions;
using System.Drawing;

namespace Peek.Core.Services;

public class HighlightService : IHighlightService, IDisposable
{
    /// <summary>How often the watchdog re-checks the tracked target window.</summary>
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromMilliseconds(600);

    private readonly IHighlightOverlay _overlay;
    private readonly WindowEnumerator _windowEnumerator;
    private bool _isVisible;
    private bool _isReady;
    private Rectangle _currentRect;
    private nint? _targetHwnd;
    private Timer? _watchdog;

    public HighlightService(IHighlightOverlay overlay, WindowEnumerator windowEnumerator)
    {
        _overlay = overlay;
        _windowEnumerator = windowEnumerator;
    }

    public void Initialize()
    {
        if (_isReady) return;

        Show(new Rectangle(-100, -100, 0, 0));
        Resume();
        _isReady = true;
    }

    public void Show(Rectangle rect, nint? targetHwnd = null)
    {
        _overlay.Show(rect);
        _currentRect = rect;
        _isVisible = true;
        _targetHwnd = targetHwnd;
        RestartWatchdog();
    }

    public void Hide()
    {
        StopWatchdog();

        if (!_isVisible) return;

        _overlay.Hide();
        _isVisible = false;
    }

    public void Resume()
    {
        if (_isVisible) return;

        _overlay.Show(_currentRect);
        _isVisible = true;
        RestartWatchdog();
    }

    public void Reset()
    {
        UpdateLocation(new Rectangle(-100, -100, 0, 0));
    }

    public void UpdateLocation(Rectangle rect, nint? targetHwnd = null)
    {
        _currentRect = rect;
        RetargetIfChanged(targetHwnd);
        _overlay.Update(rect);
    }

    public async Task UpdateLocationAsync(Rectangle rect, nint? targetHwnd = null)
    {
        _currentRect = rect;
        RetargetIfChanged(targetHwnd);
        await Task.Run(() => _overlay.Update(rect));
    }

    /// <summary>
    /// Lets the high-frequency hover path (<see cref="ElementTracker"/>'s ~25ms
    /// per-mouse-sample callback) keep the watchdog's target window in sync with what's
    /// actually being highlighted, without paying for a full <see cref="RestartWatchdog"/>
    /// (a new <see cref="Timer"/>) on every single call - only when the target genuinely
    /// changes, which for a stationary hover is rare relative to how often this runs.
    /// </summary>
    private void RetargetIfChanged(nint? targetHwnd)
    {
        if (targetHwnd is not { } hwnd || hwnd == _targetHwnd) return;

        _targetHwnd = hwnd;
        RestartWatchdog();
    }

    public void Clear()
    {
        StopWatchdog();
        _targetHwnd = null;
        _overlay.Close();
        _isReady = false;
        _isVisible = false;
    }

    public void UpdateColorsRandomly()
    {
        var rand = new Random();

        var color = System.Drawing.Color.FromArgb(
            rand.Next(50, 255),
            rand.Next(50, 255),
            rand.Next(50, 255));

        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        var fill = $"#30{color.R:X2}{color.G:X2}{color.B:X2}";

        _overlay.SetBorderColor(hex);
        _overlay.SetFillColor(fill);
    }

    public Task StartBreathAsync(CancellationToken cancellationToken)
    {
        return _overlay.StartSpeakingAsync(cancellationToken);
    }

    public Task StopBreathAsync()
    {
        return _overlay.StopSpeakingAsync();
    }

    /// <summary>
    /// (Re)starts the background poll that hides the highlight once its target window closes
    /// or is minimized - not event-driven (no single Win32 notification covers both
    /// uniformly), so a short poll is the simplest mechanism that's actually correct for both.
    /// Only runs while a targetHwnd is set - a highlight shown without one (e.g. Initialize's
    /// off-screen placeholder rect) has nothing to watch.
    /// </summary>
    /// <remarks>
    /// Deliberately does NOT also hide when Peek's own window is minimized/hidden (an earlier
    /// version of this check did, via IPeekSelfWindow.IsMinimizedOrHidden) - Peek's main
    /// window is a settings/dock panel, not the tracking engine, and minimizing it (including
    /// the ordinary "minimize to tray, keep running in the background" flow) does not pause
    /// hover tracking. Tying the highlight's visibility to the main window's state meant every
    /// tray-minimize silently killed the highlight until the next full re-scan of its target -
    /// reported as "the highlight disappears after toggling tracking off then on", since a
    /// tray-minimize commonly happens around the same time as that toggle.
    /// </remarks>
    private void RestartWatchdog()
    {
        StopWatchdog();
        if (_targetHwnd is null) return;

        _watchdog = new Timer(_ => CheckTargetStillValid(), null, WatchdogInterval, WatchdogInterval);
    }

    private void StopWatchdog()
    {
        _watchdog?.Dispose();
        _watchdog = null;
    }

    private void CheckTargetStillValid()
    {
        if (!_isVisible) return;
        if (_targetHwnd is not { } hwnd) return;

        var snapshot = _windowEnumerator.SnapshotSingle(hwnd);
        if (snapshot is null || !snapshot.IsVisible || snapshot.IsMinimized)
            Hide();
    }

    public void Dispose() => StopWatchdog();
}
