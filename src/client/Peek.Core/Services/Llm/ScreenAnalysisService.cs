using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Peek.Worker.Contracts.Llm;
using Peek.Core.Abstractions;
using Peek.Core.Services;
using Peek.Core.Services.Speech;
using Peek.Core.Settings;
using Peek.Ipc.Worker;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using System.Text;

namespace Peek.Core.Services.Llm;

/// <summary>
/// AI-narrated analysis of either the whole screen (from ScreenReaderView, excluding
/// Peek's own window - see IPeekSelfWindow) or one specific window (from
/// ElementInspectorView, via PrintWindow so Peek's own window can never leak in). A
/// singleton shared by both views' ViewModels: only one analysis runs at a time regardless
/// of which entry point started it, and its streaming state (IsAnalyzing/StreamingText) is
/// what both views' panels bind to.
/// </summary>
/// <remarks>
/// Speaks the response sentence-by-sentence as it streams in, rather than waiting for the
/// full completion - the whole reason llm.completeStream exists (see WorkerRpcChannel/
/// RpcRequestDispatcher). Three sound cues bracket the process (AiNarrationSoundPlayer):
/// one when the request is sent, one when the first sentence is ready to speak, one when
/// finished - a reading-impaired user has no progress bar to look at, so silence at any of
/// these points would otherwise be indistinguishable from "nothing is happening".
/// </remarks>
public sealed partial class ScreenAnalysisService : ReactiveObject, IDisposable
{
    private readonly WorkerConnection _workerConnection;
    private readonly ISettingsService _settings;
    private readonly AudioPlayer _audioPlayer;
    private readonly AiNarrationSoundPlayer _soundPlayer;
    private readonly AnnouncementHistoryService _history;
    private readonly IPeekSelfWindow _selfWindow;
    private readonly IAiAnalysisEffect _aiEffect;
    private readonly IAccessibilitySpeechService _speechService;
    private readonly WindowEnumerator _windowEnumerator;
    private readonly ILogger<ScreenAnalysisService> _logger;

    private CancellationTokenSource? _cts;

    [Reactive]
    private bool _isAnalyzing;

    [Reactive]
    private string _streamingText = "";

    [Reactive]
    private string? _errorMessage;

    /// <summary>
    /// True from the moment an analysis has been attempted at least once - what the
    /// result/error panel's visibility is actually bound to (see ScreenReaderView.axaml/
    /// ElementInspectorView.axaml). Binding visibility to StreamingText alone was a real
    /// bug: an error that occurs before any text ever streams (AI disabled in Settings, a
    /// screenshot/connection failure, ...) left the panel permanently hidden - the user got
    /// no feedback of any kind, visible or spoken, which is exactly backwards for an
    /// accessibility tool.
    /// </summary>
    [Reactive]
    private bool _hasResult;

    public ScreenAnalysisService(IServiceProvider serviceProvider, ILogger<ScreenAnalysisService> logger)
    {
        _workerConnection = serviceProvider.GetRequiredService<WorkerConnection>();
        _settings = serviceProvider.GetRequiredService<ISettingsService>();
        _audioPlayer = serviceProvider.GetRequiredService<AudioPlayer>();
        _soundPlayer = serviceProvider.GetRequiredService<AiNarrationSoundPlayer>();
        _history = serviceProvider.GetRequiredService<AnnouncementHistoryService>();
        _selfWindow = serviceProvider.GetRequiredService<IPeekSelfWindow>();
        _aiEffect = serviceProvider.GetRequiredService<IAiAnalysisEffect>();
        _speechService = serviceProvider.GetRequiredService<IAccessibilitySpeechService>();
        _windowEnumerator = serviceProvider.GetRequiredService<WindowEnumerator>();
        _logger = logger;
    }

    public Task AnalyzeScreenAsync() => AnalyzeAsync(hwnd: null, windowTitle: null, processName: null);

    /// <summary>
    /// <paramref name="windowTitle"/>/<paramref name="processName"/> are read directly from
    /// the OS (e.g. WindowEnumerator.SnapshotSingle(hwnd), not from whichever Inspector row
    /// happens to be selected - a selected row can be a descendant element rather than the
    /// window itself) and passed to the model as ground truth, and echoed back in the
    /// panel/spoken answer, specifically so a human watching can confirm which window was
    /// actually analyzed instead of just trusting the model's own description of it.
    /// </summary>
    public Task AnalyzeWindowAsync(nint hwnd, string? windowTitle = null, string? processName = null) =>
        AnalyzeAsync(hwnd, windowTitle, processName);

    public void Cancel() => _cts?.Cancel();

    private async Task AnalyzeAsync(nint? hwnd, string? windowTitle, string? processName)
    {
        if (IsAnalyzing) return;

        var ai = _settings.Current.Ai;
        var localization = _settings.Current.Localization;
        var primaryLanguage = SpeechLanguageDetector.ToLanguageCode(
            string.IsNullOrWhiteSpace(localization.TtsLanguage) ? localization.UiLanguage : localization.TtsLanguage);
        var culture = SpeechStrings.ResolveCulture(localization);

        if (!ai.Enabled)
        {
            await FailAsync(SpeechStrings.Get("Speech_Ai_Disabled", culture), primaryLanguage).ConfigureAwait(false);
            return;
        }

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        // Reserves the speech channel for the whole analysis, not per sentence. The narration
        // arrives from the model in pieces with real gaps between them, and an ambient
        // announcement landing in one of those gaps used to cut the answer in half - the user
        // asks a question, then loses the reply to whatever their cursor drifted over. The
        // lease also gives the "stop speaking" key a way to cancel this outright, so silence
        // requested by the user still outranks the operation that owns the channel.
        using var speechLease = _speechService.BeginExclusive("AI analysis", Cancel);

        IsAnalyzing = true;
        StreamingText = "";
        ErrorMessage = null;
        HasResult = true;
        var hidSelf = false;
        var startedAiEffect = false;

        try
        {
            byte[] imageBytes;
            if (hwnd is { } targetHwnd)
            {
                // Shows the "AI is looking at this" effect over the window being analysed.
                // Its own overlay, not the screen reader's highlight box: this is a
                // progress cue for an operation the user started, not a statement about
                // where the screen reader's attention is.
                var targetRect = _windowEnumerator.SnapshotSingle(targetHwnd)?.Rect;
                if (targetRect is { } r)
                {
                    _aiEffect.Show(new System.Drawing.Rectangle(r.Left, r.Top, r.Width, r.Height));
                    startedAiEffect = true;
                }

                var shot = await _workerConnection.Client.Screenshot.CaptureWindowAsync(targetHwnd, ct).ConfigureAwait(false);
                imageBytes = shot.ImageData;
            }
            else
            {
                await _selfWindow.HideForScreenCaptureAsync().ConfigureAwait(false);
                hidSelf = true;
                var shot = await _workerConnection.Client.Screenshot.CaptureDesktopAsync(ct).ConfigureAwait(false);
                imageBytes = shot.ImageData;
                // Cleared before actually calling RestoreAfterScreenCapture (not after) -
                // if that call itself throws, hidSelf must already read false, or the
                // finally block below retries the very call that just failed, this time
                // completely unprotected (this is exactly how a Window.Show() thread-
                // affinity exception here turned into a fatal, unhandled crash of the whole
                // app instead of a caught, logged, spoken error - see git history).
                hidSelf = false;
                _selfWindow.RestoreAfterScreenCapture();
            }

            var windowContext = hwnd is null
                ? null
                : $"Title: {(string.IsNullOrWhiteSpace(windowTitle) ? "(untitled)" : windowTitle)}\nApplication/process: {(string.IsNullOrWhiteSpace(processName) ? "(unknown)" : processName)}";

            var provider = LlmProviderConfigResolver.Resolve(_settings.Current);
            List<LlmMessage> messages =
            [
                new()
                {
                    Role = "system",
                    Content = AccessibilityPromptBuilder.BuildScreenAnalysisPrompt(ai.SystemPromptCustomization, windowContext),
                },
                new()
                {
                    Role = "user",
                    Content = hwnd is null
                        ? "Analyze this screenshot of my whole screen."
                        : $"Analyze this screenshot of the current window ({windowContext}).",
                    ImageDataBase64 = Convert.ToBase64String(imageBytes),
                    ImageMimeType = "image/png",
                },
            ];

            await _soundPlayer.PlayThinkingAsync().ConfigureAwait(false);

            var sentenceBuffer = new StringBuilder();
            var fullText = new StringBuilder();
            var isFirstSpeechSegment = true;
            var spokeAnything = false;

            await foreach (var delta in _workerConnection.Client.Llm
                .CompleteStreamAsync(provider, messages, ai.Temperature, ai.ScreenAnalysisMaxTokens, ct)
                .ConfigureAwait(false))
            {
                StreamingText += delta;
                fullText.Append(delta);
                sentenceBuffer.Append(delta);

                foreach (var sentence in ExtractCompleteSentences(sentenceBuffer))
                {
                    if (!spokeAnything)
                    {
                        await _soundPlayer.PlaySpeakingStartAsync().ConfigureAwait(false);
                        spokeAnything = true;
                    }
                    await SpeakSegmentAsync(sentence, primaryLanguage, isFirstSpeechSegment, ct).ConfigureAwait(false);
                    isFirstSpeechSegment = false;
                }
            }

            // Whatever never hit a sentence boundary (a short reply, or one not ending in
            // punctuation) still needs to be spoken.
            var remaining = sentenceBuffer.ToString().Trim();
            if (remaining.Length > 0)
            {
                if (!spokeAnything)
                    await _soundPlayer.PlaySpeakingStartAsync().ConfigureAwait(false);
                await SpeakSegmentAsync(remaining, primaryLanguage, isFirstSpeechSegment, ct).ConfigureAwait(false);
            }

            _history.Append(fullText.ToString());
            await _soundPlayer.PlayDoneAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // User cancelled (StopAnalysisCommand) - not an error.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Screen/window analysis failed");
            var message = SpeechStrings.Format("Speech_Ai_AnalysisFailed", culture, ex.Message);
            ErrorMessage = message;
            // Best-effort: the caller is already inside a catch block, and a broken TTS/
            // audio path here must not mask the real error above.
            try
            {
                await SpeakSegmentAsync(message, primaryLanguage, interruptPrevious: true, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception speakEx)
            {
                _logger.LogWarning(speakEx, "Failed to speak the analysis error message");
            }
        }
        finally
        {
            // Best-effort cleanup - must never itself throw unhandled out of a finally
            // block (that would crash the whole app, not just fail this one analysis).
            if (hidSelf)
            {
                try
                {
                    _selfWindow.RestoreAfterScreenCapture();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restore Peek's window after a screen capture - it may stay hidden until manually reopened");
                }
            }

            if (startedAiEffect)
            {
                try
                {
                    _aiEffect.Hide();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to hide the AI analysis effect");
                }
            }

            IsAnalyzing = false;
        }
    }

    /// <summary>Sets and speaks an error without ever having started a real analysis (e.g. AI disabled in Settings) - still marks HasResult so the panel shows it.</summary>
    private async Task FailAsync(string message, string primaryLanguage)
    {
        ErrorMessage = message;
        HasResult = true;
        try
        {
            await SpeakSegmentAsync(message, primaryLanguage, interruptPrevious: true, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to speak an analysis error message");
        }
    }

    /// <summary>Speaks one already-complete sentence/segment, itself further split per-language (SpeechLanguageDetector) exactly like AccessibilitySpeechService - the AI's answer can just as easily be mixed-language as any other announcement.</summary>
    private async Task SpeakSegmentAsync(string text, string primaryLanguage, bool interruptPrevious, CancellationToken ct)
    {
        var speech = _settings.Current.Speech;
        var segments = SpeechLanguageDetector.Segment(text, primaryLanguage);

        // Only the very first spoken segment of the whole narration cuts off whatever was
        // playing before it started (a stale hover announcement, say) - every later segment
        // must NOT do that, or the narration would keep interrupting itself sentence by
        // sentence.
        if (interruptPrevious)
            _audioPlayer.Stop();

        foreach (var segment in segments)
        {
            var voiceId = speech.VoiceIdByLanguage.GetValueOrDefault(segment.Language);
            var result = await _workerConnection.Client.Tts
                .SpeakAsync(segment.Text, voiceId, speech.Rate, interrupt: false, ct: ct)
                .ConfigureAwait(false);
            await _audioPlayer.PlayBytesAsync(result.AudioData, result.Format).ConfigureAwait(false);
        }
    }

    /// <summary>Pulls complete sentences off the front of <paramref name="buffer"/>, leaving any incomplete trailing text for the next chunk to extend.</summary>
    private static List<string> ExtractCompleteSentences(StringBuilder buffer)
    {
        var text = buffer.ToString();
        var sentences = new List<string>();
        var segmentStart = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('.' or '!' or '?' or '\n' or '。' or '！' or '？')) continue;

            var next = i + 1 < text.Length ? text[i + 1] : (char?)null;
            if (next is null || char.IsWhiteSpace(next.Value))
            {
                var sentence = text[segmentStart..(i + 1)].Trim();
                if (sentence.Length > 0) sentences.Add(sentence);
                segmentStart = i + 1;
            }
        }

        if (segmentStart > 0)
            buffer.Remove(0, segmentStart);

        return sentences;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
