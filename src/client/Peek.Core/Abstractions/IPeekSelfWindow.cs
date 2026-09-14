namespace Peek.Core.Abstractions;

/// <summary>
/// Lets ScreenAnalysisService hide Peek's own window(s) around a full-desktop screenshot
/// (see ScreenshotService.CaptureDesktopAsync - a raw BitBlt of the screen, it would
/// otherwise include Peek's own dock strip/window in the capture). Implemented by
/// MainWindow, which covers both shell modes - docked mode's DockShellView is hosted
/// inside it, so hiding MainWindow hides that too.
/// </summary>
public interface IPeekSelfWindow
{
    /// <summary>Hides the window and waits long enough for the desktop compositor to repaint whatever was behind it before returning.</summary>
    Task HideForScreenCaptureAsync();

    void RestoreAfterScreenCapture();

    /// <summary>
    /// True while Peek's own window is minimized or hidden (tray minimize, or the OS
    /// minimize button - both are covered). A plain cached bool, safe to read from any
    /// thread (e.g. HighlightService's background watchdog) without marshaling onto the UI
    /// thread first - see MainWindow's implementation for why a live property read isn't
    /// safe there.
    /// </summary>
    bool IsMinimizedOrHidden { get; }
}
