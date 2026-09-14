using Peek.Core.Abstractions;
using System.Drawing;

namespace Peek.Core.Services;

public class HighlightService : IHighlightService, IDisposable
{
    /// <summary>How often the watchdog re-checks the tracked target window and Peek's own window state.</summary>
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromMilliseconds(600);

    private readonly IHighlightOverlay _overlay;
    private readonly WindowEnumerator _windowEnumerator;
    private readonly IPeekSelfWindow _selfWindow;
    private bool _isVisible;
    private bool _isReady;
    private Rectangle _currentRect;
    private nint? _targetHwnd;
    private Timer? _watchdog;

    public HighlightService(IHighlightOverlay overlay, WindowEnumerator windowEnumerator, IPeekSelfWindow selfWindow)
    {
        _overlay = overlay;
        _windowEnumerator = windowEnumerator;
        _selfWindow = selfWindow;
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

    public void UpdateLocation(Rectangle rect)
    {
        _currentRect = rect;
        _overlay.Update(rect);
    }

    public async Task UpdateLocationAsync(Rectangle rect)
    {
        _currentRect = rect;
        await Task.Run(() => _overlay.Update(rect));
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
    /// (Re)starts the background poll that hides the highlight once its target window is
    /// gone - not event-driven (no single Win32 notification covers "closed" AND
    /// "minimized" AND "Peek itself got minimized" uniformly), so a short poll is the
    /// simplest mechanism that's actually correct for all three. Only runs while a
    /// targetHwnd is set - a highlight shown without one (e.g. Initialize's off-screen
    /// placeholder rect) has nothing to watch.
    /// </summary>
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

        if (_selfWindow.IsMinimizedOrHidden)
        {
            Hide();
            return;
        }

        if (_targetHwnd is not { } hwnd) return;

        var snapshot = _windowEnumerator.SnapshotSingle(hwnd);
        if (snapshot is null || !snapshot.IsVisible || snapshot.IsMinimized)
            Hide();
    }

    public void Dispose() => StopWatchdog();
}
