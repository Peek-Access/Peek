namespace Peek.Core.Services.Ocr;

/// <summary>
/// Pure, deterministic DoNotRun/SuggestOcr/RunAutomatically/RunOnDemand state
/// machine (§3/§12). Has no I/O of its own - Decide() is a function of
/// OcrDecisionContext, so it is unit-testable without UIA, a screenshot, TTS, or a
/// running Peek.Worker.
/// </summary>
public sealed class DefaultOcrDecisionService : IOcrDecisionService
{
    /// <summary>Minimum time between automatic OCR runs on an unchanged screen - avoids re-scanning the same content on every mouse move (§25).</summary>
    public TimeSpan MinRerunInterval { get; init; } = TimeSpan.FromSeconds(2);

    public OcrDecision Decide(OcrDecisionContext context)
    {
        if (context.UserPreference == OcrUserPreference.Disabled)
            return OcrDecision.DoNotRun;

        // An explicit request always runs, even over a just-completed scan - the
        // user asked for a fresh read right now.
        if (context.UserExplicitlyRequested)
            return OcrDecision.RunOnDemand;

        if (context.UserPreference == OcrUserPreference.ManualOnly)
            // Suggest whenever UIA alone isn't enough - whether because it's
            // unavailable or just came back thin - never run without being asked.
            return context.AutomationHasMeaningfulContent ? OcrDecision.DoNotRun : OcrDecision.SuggestOcr;

        var recentlyScanned = context.TimeSinceLastOcr is { } elapsed
            && elapsed < MinRerunInterval
            && !context.ScreenChangedSignificantly;

        if (!context.AutomationAvailable)
        {
            // OCR is the only source of information for this window.
            if (recentlyScanned) return OcrDecision.DoNotRun;
            return context.UserPreference == OcrUserPreference.SuggestOnly
                ? OcrDecision.SuggestOcr
                : OcrDecision.RunAutomatically;
        }

        if (!NeedsMoreThanAutomationGave(context))
            // UIA already answered the question - don't second-guess it with OCR.
            return OcrDecision.DoNotRun;

        // UIA is available but came back thin: there may be more visual text than
        // UIA exposed (§3's own example wording). Don't announce OCR activity
        // aggressively - only run automatically for windows already known to be
        // UIA-poor, and never while something is actively being spoken.
        if (context.IsKnownProblematicApplication
            && context.UserPreference == OcrUserPreference.Automatic
            && !context.IsSpeaking)
            return recentlyScanned ? OcrDecision.DoNotRun : OcrDecision.RunAutomatically;

        return OcrDecision.SuggestOcr;
    }

    private static bool NeedsMoreThanAutomationGave(OcrDecisionContext context) =>
        context.AutomationAvailable && !context.AutomationHasMeaningfulContent;
}
