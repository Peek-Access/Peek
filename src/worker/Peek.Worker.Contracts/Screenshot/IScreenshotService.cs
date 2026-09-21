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

    /// <summary>
    /// A small downsampled grayscale sample of <paramref name="hwnd"/>'s current contents -
    /// cheap enough to call every few seconds to check "did this window's content actually
    /// change" without paying for a full-resolution capture or an OCR pass.
    /// </summary>
    Task<ScreenshotFingerprintResult> CaptureFingerprintAsync(nint hwnd, CancellationToken ct = default);
}
