using System.Diagnostics;
using System.Runtime.Versioning;
using System.Speech.Synthesis;
using Microsoft.Extensions.Logging;
using Peek.Worker.Contracts.Tts;

namespace Peek.Worker.Tts.Windows;

/// <summary>
/// <see cref="ITtsService"/> on top of the voices Windows already ships (SAPI). Lower
/// quality than Piper's neural voices, and deliberately so - this exists to be the thing
/// that speaks when Piper can't.
/// </summary>
/// <remarks>
/// Piper downloads its runtime and voice models on first use (see PiperTtsService's
/// EnsurePiperInstalledAsync). That makes the very first run of a fresh install depend on a
/// working network connection, and the thing that breaks when it isn't there is the app's
/// only output channel: a blind user gets silence, with no way to hear what went wrong or
/// that anything is downloading. Windows has always-present voices, so Peek can speak
/// immediately and keep speaking while better voices arrive in the background.
/// <para>
/// No queue or interruption state of its own: synthesis here is a synchronous, in-process
/// call that produces a WAV buffer, and the client already owns playback and interruption
/// (AccessibilitySpeechService stops the player before starting the next utterance).
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class SapiTtsService : ITtsService
{
    private readonly ILogger<SapiTtsService> _logger;
    private long _utterancesSynthesized;
    private double _lastSynthesisMs;

    public SapiTtsService(ILogger<SapiTtsService> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<TtsVoiceInfo>> GetVoicesAsync(CancellationToken ct = default)
    {
        using var synthesizer = new SpeechSynthesizer();

        var voices = synthesizer.GetInstalledVoices()
            .Where(v => v.Enabled)
            .Select(v => new TtsVoiceInfo
            {
                Id = v.VoiceInfo.Name,
                Language = v.VoiceInfo.Culture.TwoLetterISOLanguageName,
                DisplayName = $"{v.VoiceInfo.Name} (Windows)",
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<TtsVoiceInfo>>(voices);
    }

    public async Task<TtsSpeakResult> SpeakAsync(TtsSpeakRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var sw = Stopwatch.StartNew();

        _logger.LogDebug("SAPI: synthesizing {Chars} chars (voice={Voice}, rate={Rate})",
            request.Text?.Length ?? 0, request.VoiceId, request.Rate);

        var audio = await RunOnStaThreadAsync(() =>
        {
            // A fresh synthesizer per utterance: SpeechSynthesizer is not thread-safe, and
            // this is a fallback path where a few ms of construction cost is irrelevant next
            // to not having to reason about shared state.
            using var synthesizer = new SpeechSynthesizer();
            using var stream = new MemoryStream();

            SelectVoiceForLanguage(synthesizer, request.VoiceId);

            // Peek's rate is a multiplier around 1.0; SAPI's is an integer -10..10 around 0.
            synthesizer.Rate = (int)Math.Clamp(Math.Round((request.Rate - 1f) * 10f), -10, 10);

            synthesizer.SetOutputToWaveStream(stream);
            synthesizer.Speak(request.Text);

            return stream.ToArray();
        }, ct).ConfigureAwait(false);

        sw.Stop();
        Interlocked.Increment(ref _utterancesSynthesized);
        _lastSynthesisMs = sw.Elapsed.TotalMilliseconds;

        _logger.LogDebug("SAPI: produced {Bytes} bytes of WAV in {Ms:F0}ms",
            audio.Length, sw.Elapsed.TotalMilliseconds);

        return new TtsSpeakResult
        {
            AudioData = audio,
            Format = "wav",
            SynthesisMs = sw.Elapsed.TotalMilliseconds,
        };
    }

    /// <summary>
    /// Runs the synthesis on a dedicated STA thread. SAPI is apartment-threaded COM, and every
    /// thread this could otherwise run on - the worker's RPC dispatch runs on the thread pool -
    /// is MTA, which leaves the CLR to marshal each call through a host-created STA behind the
    /// scenes. Owning the apartment here is both more predictable and keeps a blocking
    /// <c>Speak</c> off a pool thread, where it would occupy a worker for the whole utterance.
    /// </summary>
    private static Task<byte[]> RunOnStaThreadAsync(Func<byte[]> synthesize, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                tcs.TrySetResult(synthesize());
            }
            catch (OperationCanceledException)
            {
                tcs.TrySetCanceled(ct);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "peek-sapi-tts",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
    }

    /// <summary>
    /// Piper voice ids ("en_US-lessac-medium") mean nothing to SAPI, but their language
    /// prefix does - match an installed voice on that, so asking for a German Piper voice
    /// still gets a German Windows voice rather than an English one reading German text.
    /// </summary>
    private void SelectVoiceForLanguage(SpeechSynthesizer synthesizer, string? voiceId)
    {
        if (string.IsNullOrWhiteSpace(voiceId))
            return;

        var separatorIndex = voiceId.IndexOfAny(['_', '-']);
        var language = (separatorIndex > 0 ? voiceId[..separatorIndex] : voiceId).ToLowerInvariant();

        try
        {
            var match = synthesizer.GetInstalledVoices()
                .Where(v => v.Enabled)
                .FirstOrDefault(v => v.VoiceInfo.Culture.TwoLetterISOLanguageName.Equals(language, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
                synthesizer.SelectVoice(match.VoiceInfo.Name);
            else
                _logger.LogDebug("No Windows voice installed for '{Language}' - using the system default", language);
        }
        catch (Exception ex)
        {
            // A voice that's listed but broken shouldn't cost us the utterance - the default
            // voice is still better than silence.
            _logger.LogDebug(ex, "Could not select a Windows voice for '{Language}'", language);
        }
    }

    /// <summary>No-op: there's no queue to drain - each call synthesizes synchronously and the client owns playback.</summary>
    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<TtsStatus> GetStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new TtsStatus
        {
            IsReady = true,
            ActiveVoiceId = "windows-sapi",
            QueueDepth = 0,
            UtterancesSynthesized = Interlocked.Read(ref _utterancesSynthesized),
            LastSynthesisMs = _lastSynthesisMs,
        });
}
