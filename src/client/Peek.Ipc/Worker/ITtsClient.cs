using Peek.Worker.Contracts.Tts;

namespace Peek.Ipc.Worker;

/// <summary>
/// Typed async API for Peek.Worker's ITtsService over the peek-worker named pipe.
/// All methods throw <see cref="WorkerRpcException"/> on worker-side errors.
/// </summary>
public interface ITtsClient
{
    Task<IReadOnlyList<TtsVoiceInfo>> GetVoicesAsync(CancellationToken ct = default);

    Task<TtsSpeakResult> SpeakAsync(
        string text,
        string? voiceId = null,
        float rate = 1f,
        bool interrupt = true,
        CancellationToken ct = default);

    Task StopAsync(CancellationToken ct = default);

    Task<TtsStatus> GetStatusAsync(CancellationToken ct = default);
}
