using Microsoft.Extensions.Logging;
using Peek.Worker.Contracts.Tts;

namespace Peek.Worker.Tts;

/// <summary>
/// Speaks through <paramref name="primary"/> (Piper's neural voices) and falls back to
/// <paramref name="fallback"/> (the voices Windows already has) whenever the primary engine
/// can't deliver.
/// </summary>
/// <remarks>
/// Piper fetches its runtime and voice models on first use, so a fresh install on a machine
/// that is offline, behind a proxy, or just slow has no working speech at all - and in a
/// screen reader, the broken component is the one that would have told the user something is
/// broken. Falling back means Peek starts talking immediately on any Windows machine and
/// quietly upgrades itself to the better voices once they're available.
/// <para>
/// After a primary failure, subsequent utterances go straight to the fallback for
/// <see cref="RetryPrimaryAfter"/> rather than re-attempting (and re-timing-out) on every
/// single announcement - retrying per utterance would add the full failure latency to
/// everything the user hears. The cooldown is what lets a download that finishes later start
/// being used without a restart.
/// </para>
/// </remarks>
public sealed class FallbackTtsService : ITtsService
{
    /// <summary>How long to stay on the fallback engine after a primary failure before trying the primary again.</summary>
    public static readonly TimeSpan RetryPrimaryAfter = TimeSpan.FromMinutes(2);

    private readonly ITtsService _primary;
    private readonly ITtsService _fallback;
    private readonly ILogger<FallbackTtsService> _logger;
    private readonly TimeProvider _timeProvider;

    private long _primaryUnavailableUntilTicks;

    public FallbackTtsService(
        ITtsService primary,
        ITtsService fallback,
        ILogger<FallbackTtsService> logger,
        TimeProvider? timeProvider = null)
    {
        _primary = primary;
        _fallback = fallback;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>True while the primary engine is in its post-failure cooldown.</summary>
    public bool IsUsingFallback =>
        Interlocked.Read(ref _primaryUnavailableUntilTicks) > _timeProvider.GetUtcNow().UtcTicks;

    public async Task<TtsSpeakResult> SpeakAsync(TtsSpeakRequest request, CancellationToken ct = default)
    {
        if (!IsUsingFallback)
        {
            try
            {
                return await _primary.SpeakAsync(request, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Piper throws this not just when the caller's own `ct` fires, but also when
                // AccessibilitySpeechService interrupts an in-flight utterance with a newer one
                // (PiperTtsService's internal _currentUtteranceCts - a completely different
                // token from `ct`, so `ct.IsCancellationRequested` alone would miss this case).
                // That happens on every fast hover, i.e. constantly during normal use, and is
                // not evidence the engine is broken - treating it as a failure was disabling
                // Piper for two minutes on routine interruption and rerouting everything
                // (voices, language and all) to whatever Windows SAPI voice happens to be the
                // system default, which is a different language on a non-English Windows
                // install. Re-running the now-stale request on the fallback engine afterward
                // would be wrong too, so just propagate the cancellation.
                throw;
            }
            catch (Exception ex)
            {
                MarkPrimaryUnavailable();
                _logger.LogWarning(ex,
                    "Neural speech (Piper) failed - falling back to Windows voices for the next {Cooldown}. " +
                    "This is expected on a first run with no network: Piper downloads its runtime and voice on demand.",
                    RetryPrimaryAfter);
            }
        }

        return await _fallback.SpeakAsync(request, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TtsVoiceInfo>> GetVoicesAsync(CancellationToken ct = default)
    {
        // Both lists, always: which engine speaks a given utterance can change between calls,
        // so reporting only one engine's voices would misrepresent what's actually available.
        var voices = new List<TtsVoiceInfo>();

        try
        {
            voices.AddRange(await _primary.GetVoicesAsync(ct).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not list neural voices");
        }

        try
        {
            voices.AddRange(await _fallback.GetVoicesAsync(ct).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not list Windows voices");
        }

        return voices;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        // Stop both regardless of which one is currently preferred: a switch between engines
        // can happen mid-utterance, and "be quiet" has to mean it either way.
        try
        {
            await _primary.StopAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Stopping neural speech failed");
        }

        await _fallback.StopAsync(ct).ConfigureAwait(false);
    }

    public async Task<TtsStatus> GetStatusAsync(CancellationToken ct = default)
    {
        if (IsUsingFallback)
            return await _fallback.GetStatusAsync(ct).ConfigureAwait(false);

        try
        {
            return await _primary.GetStatusAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read neural speech status");
            return await _fallback.GetStatusAsync(ct).ConfigureAwait(false);
        }
    }

    private void MarkPrimaryUnavailable() =>
        Interlocked.Exchange(
            ref _primaryUnavailableUntilTicks,
            _timeProvider.GetUtcNow().Add(RetryPrimaryAfter).UtcTicks);
}
