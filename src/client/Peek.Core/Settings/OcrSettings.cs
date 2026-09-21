using Peek.Core.Services.Ocr;

namespace Peek.Core.Settings;

public sealed class OcrSettings
{
    public OcrUserPreference Preference { get; set; } = OcrUserPreference.Automatic;

    /// <summary>The §3 "Additional text may be available..." wording - overrides OcrSuggestionMessages.Default when set.</summary>
    public string? SuggestionMessage { get; set; }

    /// <summary>
    /// Process names (as <see cref="Services.WindowEnumerator.GetProcessName"/> returns them -
    /// no ".exe") known to expose little/no useful UI Automation content - feeds
    /// OcrDecisionContext.IsKnownProblematicApplication, matched case-insensitively by
    /// <see cref="Ocr.OcrFallbackAnnouncer"/>. Seeded with WeChat's two Windows client names
    /// (Tencent ships "Weixin.exe" for the mainland-China build, "WeChat.exe" elsewhere) - both
    /// render their message list as a single custom-drawn surface with no per-message UIA
    /// content, so hovering/focusing it announces only the window itself without this.
    /// User-extensible for any other app with the same problem.
    /// </summary>
    public List<string> KnownProblematicApplications { get; set; } = ["Weixin", "WeChat"];

    /// <summary>
    /// How much a periodic screenshot fingerprint of an already-scanned opaque window has to
    /// differ (0..1 - mean grayscale intensity difference, see
    /// Peek.Worker.Screenshot.IImageSimilarityAlgorithm.ComputeChangeRatio) from the one captured with the current OCR
    /// scan before the window is considered to have actually changed and worth re-running OCR
    /// on, rather than just aging out of the cache on a blind timer. Checked on an internal
    /// throttle while the user keeps hovering the window, never on every mouse move. Lower =
    /// more sensitive (re-scans more eagerly; more false positives from things like a blinking
    /// caret or a ticking clock); higher = fewer unnecessary re-scans but slower to notice
    /// genuinely new content.
    /// <para>
    /// 0.01 (not a round-number guess - measured against a real worker, see
    /// ScreenshotFingerprintTests): a single short line added among otherwise-unchanged content
    /// (the realistic "one new chat message" case) measures ~1.6% on this fingerprint; a much
    /// more extreme blank-vs-three-bold-lines difference only measures ~4%. A naively "obvious"
    /// default like 0.04 would have missed the realistic case entirely while looking reasonable
    /// on paper - this fingerprint is a coarse, bilinear-blurred 32x24 grayscale downsample (see
    /// WindowsScreenshotService.ComputeFingerprint), so even a real, meaningful content change
    /// covers only a small fraction of the total average intensity. 0.01 sits with margin below
    /// the measured single-line signal and, per the same tests, far above this mechanism's
    /// measured near-zero noise floor for unchanged, non-animated content.
    /// </para>
    /// </summary>
    public double ScreenChangeThreshold { get; set; } = 0.01;
}
