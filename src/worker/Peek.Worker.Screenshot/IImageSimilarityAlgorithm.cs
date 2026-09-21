namespace Peek.Worker.Screenshot;

/// <summary>
/// Decides "has this image changed enough to matter" cheaply - the mechanism
/// Peek.Core.Services.Ocr.OcrFallbackAnnouncer (client) and
/// Peek.Worker.Screenshot.Windows.WindowsScreenshotService (worker, via
/// screenshot.captureFingerprint) build on to avoid re-running a full screenshot+OCR pass
/// every time a hovered window is merely re-checked, instead of actually redrawn
/// (see docs/OCR_STRATEGY.md, finding 8).
/// </summary>
/// <remarks>
/// Pure and platform-neutral on purpose (no System.Drawing/GDI dependency here) - so it can
/// be unit-tested with synthetic pixel data and swapped for a different technique later (a
/// perceptual hash, SSIM, ...) without either caller changing. The default implementation,
/// <see cref="GrayscaleDownsampleSimilarityAlgorithm"/>, downsamples to a small grayscale
/// grid and compares grids; see its own remarks for why, and
/// Peek.Core.Settings.OcrSettings.ScreenChangeThreshold for how its output gets turned into
/// a re-scan decision.
/// </remarks>
public interface IImageSimilarityAlgorithm
{
    /// <summary>
    /// Reduces an image to a compact fingerprint good only for
    /// <see cref="ComputeChangeRatio"/> comparisons against another fingerprint from the same
    /// algorithm - never for display, storage, or any other image processing.
    /// </summary>
    /// <param name="bgraPixels">
    /// Tightly-packed BGRA8888 bytes (blue, green, red, alpha per pixel, no padding), exactly
    /// <paramref name="width"/> * <paramref name="height"/> * 4 bytes long, row-major from the
    /// top-left pixel.
    /// </param>
    byte[] ComputeFingerprint(ReadOnlySpan<byte> bgraPixels, int width, int height);

    /// <summary>
    /// How much two fingerprints from <see cref="ComputeFingerprint"/> differ, normalized to
    /// 0 (identical) .. 1 (maximally different).
    /// </summary>
    double ComputeChangeRatio(byte[] before, byte[] after);
}
