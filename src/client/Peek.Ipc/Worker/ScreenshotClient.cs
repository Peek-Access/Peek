using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Peek.Worker.Contracts.Rpc;
using Peek.Worker.Contracts.Screenshot;

namespace Peek.Ipc.Worker;

public sealed class ScreenshotClient : IScreenshotClient
{
    // A full-desktop capture (multi-monitor, encoded to PNG) can take noticeably
    // longer than the automation channel's default RPC timeout.
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(15);

    // A fingerprint capture does the same PrintWindow/CopyFromScreen work as a full window
    // capture (that's the slow part, not the encoding), so it needs more than the default RPC
    // timeout too - just less than CaptureTimeout, since callers poll this repeatedly while
    // hovering and shouldn't be left waiting as long as a one-off full capture would allow.
    private static readonly TimeSpan FingerprintTimeout = TimeSpan.FromSeconds(5);

    private readonly WorkerRpcChannel _channel;
    private readonly ILogger<ScreenshotClient> _logger;

    public ScreenshotClient(WorkerRpcChannel channel, ILogger<ScreenshotClient> logger)
    {
        _channel = channel;
        _logger = logger;
    }

    public async Task<ScreenshotResult> CaptureDesktopAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _channel.CallAsync("screenshot.captureDesktop", timeout: CaptureTimeout, ct: ct).ConfigureAwait(false);
            response.ThrowIfError();

            return response.Deserialize(WorkerJsonContext.Default.ScreenshotResult)
                ?? throw new InvalidOperationException("Worker returned an empty screenshot response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CaptureDesktopAsync failed");
            throw;
        }
    }

    public async Task<ScreenshotResult> CaptureWindowAsync(nint hwnd, CancellationToken ct = default)
    {
        var paramsJson = JsonSerializer.SerializeToElement(
            new CaptureWindowParams { Hwnd = hwnd },
            WorkerJsonContext.Default.CaptureWindowParams);

        var response = await _channel.CallAsync("screenshot.captureWindow", paramsJson, CaptureTimeout, ct).ConfigureAwait(false);
        response.ThrowIfError();

        return response.Deserialize(WorkerJsonContext.Default.ScreenshotResult)
            ?? throw new InvalidOperationException("Worker returned an empty screenshot response");
    }

    public async Task<ScreenshotFingerprintResult> CaptureFingerprintAsync(nint hwnd, CancellationToken ct = default)
    {
        var paramsJson = JsonSerializer.SerializeToElement(
            new CaptureWindowParams { Hwnd = hwnd },
            WorkerJsonContext.Default.CaptureWindowParams);

        var response = await _channel.CallAsync("screenshot.captureFingerprint", paramsJson, FingerprintTimeout, ct).ConfigureAwait(false);
        response.ThrowIfError();

        return response.Deserialize(WorkerJsonContext.Default.ScreenshotFingerprintResult)
            ?? throw new InvalidOperationException("Worker returned an empty fingerprint response");
    }
}
