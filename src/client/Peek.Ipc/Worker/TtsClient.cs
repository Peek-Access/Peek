using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Peek.Worker.Contracts.Rpc;
using Peek.Worker.Contracts.Tts;

namespace Peek.Ipc.Worker;

public sealed class TtsClient : ITtsClient
{
    // Synthesis (spawns the piper process + model load) legitimately takes longer
    // than the automation channel's default RPC timeout - and scales with text
    // length, so a longer LLM-generated description (§13) needs real headroom here,
    // not just a cold-start allowance. Matches LlmClient's own CompleteTimeout.
    private static readonly TimeSpan SpeakTimeout = TimeSpan.FromSeconds(60);

    private readonly WorkerRpcChannel _channel;
    private readonly ILogger<TtsClient> _logger;

    public TtsClient(WorkerRpcChannel channel, ILogger<TtsClient> logger)
    {
        _channel = channel;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TtsVoiceInfo>> GetVoicesAsync(CancellationToken ct = default)
    {
        var response = await _channel.CallAsync("tts.getVoices", ct: ct).ConfigureAwait(false);
        response.ThrowIfError();
        var result = response.Deserialize(WorkerJsonContext.Default.VoicesResult);
        return result?.Voices ?? [];
    }

    public async Task<TtsSpeakResult> SpeakAsync(
        string text,
        string? voiceId = null,
        float rate = 1f,
        bool interrupt = true,
        CancellationToken ct = default)
    {
        var paramsJson = JsonSerializer.SerializeToElement(
            new SpeakParams { Text = text, VoiceId = voiceId, Rate = rate, Interrupt = interrupt },
            WorkerJsonContext.Default.SpeakParams);

        var response = await _channel.CallAsync("tts.speak", paramsJson, SpeakTimeout, ct).ConfigureAwait(false);
        response.ThrowIfError();

        return response.Deserialize(WorkerJsonContext.Default.TtsSpeakResult)
            ?? throw new InvalidOperationException("Worker returned an empty speak response");
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        var response = await _channel.CallAsync("tts.stop", ct: ct).ConfigureAwait(false);
        response.ThrowIfError();
    }

    public async Task<TtsStatus> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _channel.CallAsync("tts.getStatus", ct: ct).ConfigureAwait(false);
            response.ThrowIfError();

            return response.Deserialize(WorkerJsonContext.Default.TtsStatus)
                ?? throw new InvalidOperationException("Worker returned an empty status response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetStatusAsync failed");
            throw;
        }
    }
}
