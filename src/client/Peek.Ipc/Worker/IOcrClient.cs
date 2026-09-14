using Peek.Worker.Contracts.Ocr;

namespace Peek.Ipc.Worker;

/// <summary>
/// Typed async API for Peek.Worker's IOcrService over the peek-worker named pipe.
/// All methods throw <see cref="WorkerRpcException"/> on worker-side errors.
/// </summary>
public interface IOcrClient
{
    Task<OcrResult> RecognizeAsync(
        byte[] pngImage,
        OcrRegion? region = null,
        CancellationToken ct = default);

    Task<OcrStatus> GetStatusAsync(CancellationToken ct = default);
}
