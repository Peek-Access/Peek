namespace Peek.Core.Services.Ocr;

/// <summary>
/// Wording spoken/shown for a <see cref="OcrDecision.SuggestOcr"/> decision (§3:
/// "the system could say something similar to..."). A single configurable default
/// for now; full localization through the existing Irihi.Lingua resx pipeline is
/// Phase 6 (the real Settings/localization model) - this is the extension point.
/// </summary>
public static class OcrSuggestionMessages
{
    public static string Default { get; set; } =
        "Additional text may be available. Press the OCR shortcut to scan the screen.";
}
