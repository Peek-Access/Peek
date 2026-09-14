namespace Peek.Core.Services.Ocr;

/// <summary>The accessibility-oriented OCR decision state machine (§3/§12).</summary>
public enum OcrDecision
{
    DoNotRun,
    SuggestOcr,
    RunAutomatically,
    RunOnDemand,
}

/// <summary>User-configured OCR posture. Full Settings UI is Phase 6 (§17 OcrSettings) - this enum is the extension point.</summary>
public enum OcrUserPreference
{
    Automatic,
    SuggestOnly,
    ManualOnly,
    Disabled,
}
