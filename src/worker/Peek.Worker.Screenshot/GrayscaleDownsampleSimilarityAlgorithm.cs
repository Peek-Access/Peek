namespace Peek.Worker.Screenshot;

/// <summary>
/// Downsamples to a fixed, tiny grayscale grid via box-filter averaging, then compares grids
/// by mean absolute difference. The default (and, today, only) <see cref="IImageSimilarityAlgorithm"/>.
/// </summary>
/// <remarks>
/// The grid is small and coarse - <see cref="FingerprintWidth"/> x <see cref="FingerprintHeight"/>,
/// picked to stay cheap enough to send over the worker/client pipe every few seconds - and
/// averaging every source pixel that falls into a cell (rather than nearest-neighbor
/// sampling) is what makes it tolerant of ordinary rendering noise: antialiasing jitter or a
/// blinking caret shifts a handful of pixels inside a cell, which mostly washes out in that
/// cell's average instead of swinging the whole comparison. This is a coarse, deliberately
/// simple technique (closer to an average hash than a perceptual one) - good enough for
/// "did this window's content change" without needing to identify what changed, which is what
/// OCR itself is for. Calibrated defaults for its output live on
/// Peek.Core.Settings.OcrSettings.ScreenChangeThreshold - see its remarks and
/// ScreenshotFingerprintTests for the measurements behind them.
/// </remarks>
public sealed class GrayscaleDownsampleSimilarityAlgorithm : IImageSimilarityAlgorithm
{
    public const int FingerprintWidth = 32;
    public const int FingerprintHeight = 24;

    public byte[] ComputeFingerprint(ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Width and height must both be positive.");

        var required = checked(width * height * 4);
        if (bgraPixels.Length < required)
            throw new ArgumentException(
                $"Pixel buffer is {bgraPixels.Length} bytes, expected at least {required} (width * height * 4).",
                nameof(bgraPixels));

        var fingerprint = new byte[FingerprintWidth * FingerprintHeight];

        for (var ty = 0; ty < FingerprintHeight; ty++)
        {
            var srcY0 = (int)((long)ty * height / FingerprintHeight);
            var srcY1 = Math.Max(srcY0 + 1, (int)((long)(ty + 1) * height / FingerprintHeight));

            for (var tx = 0; tx < FingerprintWidth; tx++)
            {
                var srcX0 = (int)((long)tx * width / FingerprintWidth);
                var srcX1 = Math.Max(srcX0 + 1, (int)((long)(tx + 1) * width / FingerprintWidth));

                long sum = 0;
                long count = 0;

                for (var y = srcY0; y < srcY1; y++)
                {
                    var rowStart = y * width * 4;
                    for (var x = srcX0; x < srcX1; x++)
                    {
                        var i = rowStart + x * 4;
                        var b = bgraPixels[i];
                        var g = bgraPixels[i + 1];
                        var r = bgraPixels[i + 2];
                        // Standard luma weights (Rec. 601) - good enough for a
                        // change-detection fingerprint, no need for anything more
                        // perceptually accurate here.
                        sum += (r * 30 + g * 59 + b * 11) / 100;
                        count++;
                    }
                }

                fingerprint[ty * FingerprintWidth + tx] = (byte)(sum / count);
            }
        }

        return fingerprint;
    }

    public double ComputeChangeRatio(byte[] before, byte[] after)
    {
        // A length mismatch means "can't tell", and the safe direction is to trigger a
        // re-scan rather than silently keep trusting a stale cached one.
        if (before.Length != after.Length || before.Length == 0) return 1.0;

        long sum = 0;
        for (var i = 0; i < before.Length; i++)
            sum += Math.Abs(before[i] - after[i]);

        return sum / (double)before.Length / 255.0;
    }
}
