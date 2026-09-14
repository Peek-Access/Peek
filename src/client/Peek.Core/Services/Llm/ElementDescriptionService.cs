using Microsoft.Extensions.Logging;
using Peek.Worker.Contracts.Automation;
using Peek.Core.Services.Ocr;
using Peek.Core.Services.Speech;
using Peek.Core.Settings;
using Peek.Ipc.Worker;

namespace Peek.Core.Services.Llm;

public sealed class ElementDescriptionService : IElementDescriptionService
{
    private readonly WorkerConnection _workerConnection;
    private readonly IContextAggregator _contextAggregator;
    private readonly IOcrDecisionService _ocrDecisionService;
    private readonly IAccessibilitySpeechService _speechService;
    private readonly ISettingsService _settings;
    private readonly ILogger<ElementDescriptionService> _logger;

    public ElementDescriptionService(
        WorkerConnection workerConnection,
        IContextAggregator contextAggregator,
        IOcrDecisionService ocrDecisionService,
        IAccessibilitySpeechService speechService,
        ISettingsService settings,
        ILogger<ElementDescriptionService> logger)
    {
        _workerConnection = workerConnection;
        _contextAggregator = contextAggregator;
        _ocrDecisionService = ocrDecisionService;
        _speechService = speechService;
        _settings = settings;
        _logger = logger;
    }

    public async Task DescribeAsync(SemanticElement element, CancellationToken ct = default)
    {
        var ai = _settings.Current.Ai;
        if (!ai.Enabled)
        {
            await _speechService.AnnounceTextAsync("AI description is turned off in settings.", SpeechPriority.UserRequested, ct).ConfigureAwait(false);
            return;
        }

        // Explicit user request, so OcrDecisionService always says RunOnDemand
        // (§3/§12) - still routed through it rather than skipped, so the decision
        // is made in one place and the same rules apply everywhere it's asked.
        var decision = _ocrDecisionService.Decide(new OcrDecisionContext
        {
            AutomationAvailable = true,
            AutomationHasMeaningfulContent = !string.IsNullOrWhiteSpace(element.Name),
            UserExplicitlyRequested = true,
            UserPreference = _settings.Current.Ocr.Preference,
        });

        string? ocrText = null;
        if (decision is OcrDecision.RunOnDemand or OcrDecision.RunAutomatically)
            ocrText = await TryOcrWindowAsync(element.Hwnd, ct).ConfigureAwait(false);

        var messages = _contextAggregator.BuildMessages(new LlmContext
        {
            FocusedElement = element,
            OcrText = ocrText,
            UserCommand = "In one or two short sentences, explain what this UI element is and what the user can do with it.",
            UserSystemPromptCustomization = ai.SystemPromptCustomization,
        });

        try
        {
            var provider = LlmProviderConfigResolver.Resolve(_settings.Current);
            var response = await _workerConnection.Client.Llm
                .CompleteAsync(provider, messages, ai.Temperature, ai.MaxTokens, ct)
                .ConfigureAwait(false);
            await _speechService.AnnounceTextAsync(response.Text, SpeechPriority.UserRequested, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM description failed for element: {Name}", element.Name);
            await _speechService.AnnounceTextAsync("Sorry, I couldn't get a description right now.", SpeechPriority.UserRequested, ct).ConfigureAwait(false);
        }
    }

    private async Task<string?> TryOcrWindowAsync(nint hwnd, CancellationToken ct)
    {
        try
        {
            var screenshot = await _workerConnection.Client.Screenshot.CaptureWindowAsync(hwnd, ct).ConfigureAwait(false);
            var ocrResult = await _workerConnection.Client.Ocr.RecognizeAsync(screenshot.ImageData, ct: ct).ConfigureAwait(false);
            return ocrResult.Text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Screenshot/OCR failed while describing element - continuing with UIA data only");
            return null;
        }
    }
}
