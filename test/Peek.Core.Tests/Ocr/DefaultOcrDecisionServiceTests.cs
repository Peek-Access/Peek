using Peek.Core.Services.Ocr;
using Xunit;

namespace Peek.Core.Tests.Ocr;

public class DefaultOcrDecisionServiceTests
{
    private readonly DefaultOcrDecisionService _service = new();

    [Fact]
    public void UIA_with_meaningful_content_never_runs_OCR()
    {
        var decision = _service.Decide(new OcrDecisionContext
        {
            AutomationAvailable = true,
            AutomationHasMeaningfulContent = true,
        });

        Assert.Equal(OcrDecision.DoNotRun, decision);
    }

    [Fact]
    public void No_UIA_at_all_runs_OCR_automatically_the_first_time()
    {
        var decision = _service.Decide(new OcrDecisionContext
        {
            AutomationAvailable = false,
            AutomationHasMeaningfulContent = false,
        });

        Assert.Equal(OcrDecision.RunAutomatically, decision);
    }

    [Fact]
    public void No_UIA_recently_scanned_and_unchanged_does_not_rerun()
    {
        var decision = _service.Decide(new OcrDecisionContext
        {
            AutomationAvailable = false,
            AutomationHasMeaningfulContent = false,
            TimeSinceLastOcr = TimeSpan.FromMilliseconds(500),
            ScreenChangedSignificantly = false,
        });

        Assert.Equal(OcrDecision.DoNotRun, decision);
    }

    [Fact]
    public void No_UIA_recently_scanned_but_screen_changed_reruns()
    {
        var decision = _service.Decide(new OcrDecisionContext
        {
            AutomationAvailable = false,
            AutomationHasMeaningfulContent = false,
            TimeSinceLastOcr = TimeSpan.FromMilliseconds(500),
            ScreenChangedSignificantly = true,
        });

        Assert.Equal(OcrDecision.RunAutomatically, decision);
    }

    [Fact]
    public void Thin_UIA_on_a_known_problematic_app_runs_automatically_when_not_speaking()
    {
        var decision = _service.Decide(new OcrDecisionContext
        {
            AutomationAvailable = true,
            AutomationHasMeaningfulContent = false,
            IsKnownProblematicApplication = true,
            IsSpeaking = false,
        });

        Assert.Equal(OcrDecision.RunAutomatically, decision);
    }

    [Fact]
    public void Thin_UIA_on_a_known_problematic_app_only_suggests_while_speaking()
    {
        var decision = _service.Decide(new OcrDecisionContext
        {
            AutomationAvailable = true,
            AutomationHasMeaningfulContent = false,
            IsKnownProblematicApplication = true,
            IsSpeaking = true,
        });

        Assert.Equal(OcrDecision.SuggestOcr, decision);
    }

    [Fact]
    public void Thin_UIA_on_an_ordinary_app_only_suggests()
    {
        var decision = _service.Decide(new OcrDecisionContext
        {
            AutomationAvailable = true,
            AutomationHasMeaningfulContent = false,
            IsKnownProblematicApplication = false,
        });

        Assert.Equal(OcrDecision.SuggestOcr, decision);
    }

    [Fact]
    public void Explicit_request_wins_over_everything_except_disabled()
    {
        var decision = _service.Decide(new OcrDecisionContext
        {
            AutomationAvailable = true,
            AutomationHasMeaningfulContent = true,
            UserExplicitlyRequested = true,
        });

        Assert.Equal(OcrDecision.RunOnDemand, decision);
    }

    [Fact]
    public void Disabled_preference_wins_even_over_an_explicit_request()
    {
        var decision = _service.Decide(new OcrDecisionContext
        {
            AutomationAvailable = false,
            AutomationHasMeaningfulContent = false,
            UserExplicitlyRequested = true,
            UserPreference = OcrUserPreference.Disabled,
        });

        Assert.Equal(OcrDecision.DoNotRun, decision);
    }

    [Fact]
    public void ManualOnly_suggests_when_UIA_is_thin_but_never_runs_on_its_own()
    {
        var decision = _service.Decide(new OcrDecisionContext
        {
            AutomationAvailable = false,
            AutomationHasMeaningfulContent = false,
            UserPreference = OcrUserPreference.ManualOnly,
        });

        Assert.Equal(OcrDecision.SuggestOcr, decision);
    }

    [Fact]
    public void ManualOnly_does_nothing_when_UIA_already_has_enough()
    {
        var decision = _service.Decide(new OcrDecisionContext
        {
            AutomationAvailable = true,
            AutomationHasMeaningfulContent = true,
            UserPreference = OcrUserPreference.ManualOnly,
        });

        Assert.Equal(OcrDecision.DoNotRun, decision);
    }
}
