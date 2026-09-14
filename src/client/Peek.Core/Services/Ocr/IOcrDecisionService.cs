namespace Peek.Core.Services.Ocr;

/// <summary>
/// Decides whether OCR should run for the current UI state (§3/§12). Deliberately
/// separate from IOcrClient/IOcrService - this only decides, it never triggers a
/// screenshot or an OCR call itself, so it stays trivially unit-testable.
/// </summary>
public interface IOcrDecisionService
{
    OcrDecision Decide(OcrDecisionContext context);
}
