using Peek.Worker.Contracts.Screenshot;

namespace Peek.Ipc.Worker;

/// <summary>
/// Typed async API for Peek.Worker's IScreenshotService over the peek-worker
/// named pipe. All methods throw <see cref="WorkerRpcException"/> on worker-side errors.
/// </summary>
public interface IScreenshotClient
{
    Task<ScreenshotResult> CaptureDesktopAsync(CancellationToken ct = default);

    Task<ScreenshotResult> CaptureWindowAsync(nint hwnd, CancellationToken ct = default);

    Task<ScreenshotFingerprintResult> CaptureFingerprintAsync(nint hwnd, CancellationToken ct = default);
}
