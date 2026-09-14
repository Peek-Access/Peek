namespace Peek.Core.Services.Ocr;

/// <summary>
/// The signals IOcrDecisionService.Decide reasons over (§3/§12). Built by the
/// caller from whatever it currently knows - this type has no dependency on UI
/// Automation, screenshot, or speech types, so the decision engine stays
/// independently testable without a running worker.
/// </summary>
public sealed record OcrDecisionContext
{
    /// <summary>Whether UI Automation could be queried at all for the current window.</summary>
    public required bool AutomationAvailable { get; init; }

    /// <summary>Whether the UIA data actually retrieved looks like enough to answer the user's question (non-empty name/value/text content).</summary>
    public required bool AutomationHasMeaningfulContent { get; init; }

    /// <summary>Whether the current application/window is on a known-UIA-poor list (e.g. a canvas-rendered app).</summary>
    public bool IsKnownProblematicApplication { get; init; }

    /// <summary>Whether the user pressed the OCR shortcut / explicitly asked for a scan right now.</summary>
    public bool UserExplicitlyRequested { get; init; }

    /// <summary>Whether the screen content changed enough since the last OCR run to justify a re-scan.</summary>
    public bool ScreenChangedSignificantly { get; init; }

    /// <summary>How long ago OCR last ran for this window/region, or null if never.</summary>
    public TimeSpan? TimeSinceLastOcr { get; init; }

    /// <summary>Whether TTS is currently speaking - automatic OCR should not interrupt it with a fresh announcement.</summary>
    public bool IsSpeaking { get; init; }

    public OcrUserPreference UserPreference { get; init; } = OcrUserPreference.Automatic;
}
