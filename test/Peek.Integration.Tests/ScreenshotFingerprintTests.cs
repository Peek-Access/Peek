using Peek.Worker.Screenshot;
using Xunit;

namespace Peek.Integration.Tests;

/// <summary>
/// Exercises screenshot.captureFingerprint against a real worker and real (off-screen)
/// windows - the mechanism OcrFallbackAnnouncer.MaybeRefreshOnScreenChangeAsync uses to notice
/// an already-scanned window's content changed without paying for a full OCR re-run every time
/// it checks (see docs/OCR_STRATEGY.md, finding 8). Compares the fingerprints it gets back
/// using the same IImageSimilarityAlgorithm implementation production code is wired to
/// (GrayscaleDownsampleSimilarityAlgorithm) - a plain new(), not resolved through DI, since it's
/// pure/stateless and this is exactly the kind of thing IImageSimilarityAlgorithm exists to let
/// a test pin down against a *specific* algorithm regardless of what's registered elsewhere.
/// </summary>
public sealed class ScreenshotFingerprintTests : IClassFixture<TestWorkerFixture>
{
    private readonly TestWorkerFixture _fixture;
    private readonly IImageSimilarityAlgorithm _similarity = new GrayscaleDownsampleSimilarityAlgorithm();

    public ScreenshotFingerprintTests(TestWorkerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task An_unchanged_window_produces_a_near_identical_fingerprint()
    {
        using var window = new OpaqueTestWindow("Fingerprint Stable", "Some static text");

        // Warm-up, discarded: OpaqueTestWindow signals ready on Form.Shown, which can fire
        // slightly ahead of its first real WM_PAINT completing - capturing immediately can
        // catch that not-yet-painted frame, which then looks "different" from every capture
        // after it even though nothing the user would ever see actually changed. Production
        // code never hits this: MaybeRefreshOnScreenChangeAsync only ever compares against a
        // fingerprint taken alongside a real OCR scan, by which point the window has
        // necessarily already painted at least once.
        _ = await _fixture.Connection.Client.Screenshot.CaptureFingerprintAsync(window.Hwnd);

        var first = await _fixture.Connection.Client.Screenshot.CaptureFingerprintAsync(window.Hwnd);
        var second = await _fixture.Connection.Client.Screenshot.CaptureFingerprintAsync(window.Hwnd);

        var changeRatio = _similarity.ComputeChangeRatio(first.Fingerprint, second.Fingerprint);

        // Measured ~0% for this synthetic, non-animated GDI window (PrintWindow is fully
        // deterministic against static content with nothing on it that would tick or blink) -
        // asserting well above that, not against the exact measurement, since a real app's
        // rendering noise floor (antialiasing, a blinking caret) isn't something this synthetic
        // window can reproduce.
        Assert.True(changeRatio < 0.005,
            $"Expected a near-zero change ratio for an unchanged window, got {changeRatio:P1}");
    }

    [Fact]
    public async Task Visibly_different_windows_produce_a_meaningfully_different_fingerprint()
    {
        using var blank = new OpaqueTestWindow("Fingerprint Blank");
        using var textHeavy = new OpaqueTestWindow("Fingerprint Text", "AAAAAAAAAA", "BBBBBBBBBB", "CCCCCCCCCC");

        var blankFingerprint = await _fixture.Connection.Client.Screenshot.CaptureFingerprintAsync(blank.Hwnd);
        var textFingerprint = await _fixture.Connection.Client.Screenshot.CaptureFingerprintAsync(textHeavy.Hwnd);

        var changeRatio = _similarity.ComputeChangeRatio(blankFingerprint.Fingerprint, textFingerprint.Fingerprint);

        // Deliberately loose: this fingerprint is a coarse 32x24 grayscale downsample (see
        // GrayscaleDownsampleSimilarityAlgorithm.ComputeFingerprint) of a mostly-white window
        // with three lines of bold text - the box-averaged glyph strokes over a light background
        // produce a surprisingly small ratio even for this extreme a difference (measured ~4%), nowhere
        // near what a naive "how different do these look to a person" guess would suggest. That
        // measurement is exactly what calibrated OcrSettings.ScreenChangeThreshold's default -
        // see the remarks there.
        Assert.True(changeRatio > 0.02,
            $"Expected a meaningful change ratio between a blank and text-heavy window, got {changeRatio:P1}");
    }

    [Fact]
    public async Task A_single_added_line_among_unchanged_ones_still_registers_as_changed()
    {
        // The realistic case this whole mechanism exists for: a chat window where most of the
        // visible content (here, two of three lines) stays exactly the same and only one new
        // line appears - smaller than the blank-vs-text-heavy case above, so this is what
        // actually exercises whether the default threshold is sensitive enough to catch it.
        using var before = new OpaqueTestWindow("Fingerprint Before", "First message here", "Second message here");
        using var after = new OpaqueTestWindow("Fingerprint After", "First message here", "Second message here", "Third message here");

        var beforeFingerprint = await _fixture.Connection.Client.Screenshot.CaptureFingerprintAsync(before.Hwnd);
        var afterFingerprint = await _fixture.Connection.Client.Screenshot.CaptureFingerprintAsync(after.Hwnd);

        var changeRatio = _similarity.ComputeChangeRatio(beforeFingerprint.Fingerprint, afterFingerprint.Fingerprint);

        // Measured ~1.6% - this is the number that actually calibrated
        // OcrSettings.ScreenChangeThreshold's default (see its remarks): the blank-vs-text-heavy
        // case above measures a much larger ~4%, which would be a badly miscalibrated default on
        // its own - a single new line is the realistic case (one more chat message arriving)
        // this whole mechanism exists to catch.
        Assert.True(changeRatio > 0.005,
            $"Expected a single added line to register above a near-zero noise floor, got {changeRatio:P1}");
    }
}
