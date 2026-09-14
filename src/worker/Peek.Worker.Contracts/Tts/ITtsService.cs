namespace Peek.Worker.Contracts.Tts;

/// <summary>
/// Cross-platform text-to-speech capability. Implementations own model lifetime,
/// queueing, and interruption; callers never see engine-specific types (§2/§5 of the
/// architecture brief - PiperSharp specifics must not leak past Peek.Worker.Tts).
/// </summary>
public interface ITtsService
{
    Task<IReadOnlyList<TtsVoiceInfo>> GetVoicesAsync(CancellationToken ct = default);

    /// <summary>
    /// Synthesizes speech for <paramref name="request"/>. When
    /// <see cref="TtsSpeakRequest.Interrupt"/> is set, any not-yet-started queued
    /// utterance is dropped before this one is enqueued.
    /// </summary>
    Task<TtsSpeakResult> SpeakAsync(TtsSpeakRequest request, CancellationToken ct = default);

    /// <summary>Cancels the in-flight utterance (if any) and clears the pending queue.</summary>
    Task StopAsync(CancellationToken ct = default);

    Task<TtsStatus> GetStatusAsync(CancellationToken ct = default);
}
