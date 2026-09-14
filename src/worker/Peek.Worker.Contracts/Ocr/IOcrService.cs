namespace Peek.Worker.Contracts.Ocr;

/// <summary>
/// Cross-platform OCR capability. Implementations own model lifetime and caching;
/// callers never see engine-specific types (§5 - SimdPaddleOCR specifics must not
/// leak past Peek.Worker.Ocr). OCR is deliberately request/response, not
/// continuously running - see IOcrDecisionService (Peek.Core) for when to call it.
/// </summary>
public interface IOcrService
{
    /// <summary>
    /// Recognizes text in <paramref name="request"/>'s image. Identical
    /// (image, region) requests made in quick succession may return a cached
    /// result instead of re-running inference (§3/§25).
    /// </summary>
    Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken ct = default);

    Task<OcrStatus> GetStatusAsync(CancellationToken ct = default);
}
