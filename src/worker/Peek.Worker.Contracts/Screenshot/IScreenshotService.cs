namespace Peek.Worker.Contracts.Screenshot;

/// <summary>
/// Screen/window capture. Windows-only today (see Peek.Worker.Screenshot.Windows) but
/// isolated behind this interface so Linux/macOS implementations can be added later
/// without touching callers (§19/§5).
/// </summary>
public interface IScreenshotService
{
    Task<ScreenshotResult> CaptureDesktopAsync(CancellationToken ct = default);

    Task<ScreenshotResult> CaptureWindowAsync(nint hwnd, CancellationToken ct = default);
}
