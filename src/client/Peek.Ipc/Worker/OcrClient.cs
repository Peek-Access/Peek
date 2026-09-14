using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Peek.Worker.Contracts.Ocr;
using Peek.Worker.Contracts.Rpc;

namespace Peek.Ipc.Worker;

public sealed class OcrClient : IOcrClient
{
    // A full-screen recognition can take noticeably longer than the automation
    // channel's default RPC timeout.
    private static readonly TimeSpan RecognizeTimeout = TimeSpan.FromSeconds(15);

    private readonly WorkerRpcChannel _channel;
    private readonly ILogger<OcrClient> _logger;

    public OcrClient(WorkerRpcChannel channel, ILogger<OcrClient> logger)
    {
        _channel = channel;
        _logger = logger;
    }

    public async Task<OcrResult> RecognizeAsync(
        byte[] pngImage,
        OcrRegion? region = null,
        CancellationToken ct = default)
    {
        var paramsJson = JsonSerializer.SerializeToElement(
            new RecognizeParams { ImageData = pngImage, Region = region },
            WorkerJsonContext.Default.RecognizeParams);

        var response = await _channel.CallAsync("ocr.recognize", paramsJson, RecognizeTimeout, ct).ConfigureAwait(false);
        response.ThrowIfError();

        return response.Deserialize(WorkerJsonContext.Default.OcrResult)
            ?? throw new InvalidOperationException("Worker returned an empty OCR response");
    }

    public async Task<OcrStatus> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _channel.CallAsync("ocr.getStatus", ct: ct).ConfigureAwait(false);
            response.ThrowIfError();

            return response.Deserialize(WorkerJsonContext.Default.OcrStatus)
                ?? throw new InvalidOperationException("Worker returned an empty status response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetStatusAsync failed");
            throw;
        }
    }
}
