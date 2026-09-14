using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Peek.Worker.Contracts.Llm;
using Peek.Worker.Contracts.Rpc;

namespace Peek.Ipc.Worker;

public sealed class LlmClient : ILlmClient
{
    // Local LLM inference (cold model load especially) can take much longer than
    // the automation channel's default RPC timeout.
    private static readonly TimeSpan CompleteTimeout = TimeSpan.FromSeconds(60);

    private readonly WorkerRpcChannel _channel;
    private readonly ILogger<LlmClient> _logger;

    public LlmClient(WorkerRpcChannel channel, ILogger<LlmClient> logger)
    {
        _channel = channel;
        _logger = logger;
    }

    public async Task<LlmResponse> CompleteAsync(
        LlmProviderConfig provider,
        IReadOnlyList<LlmMessage> messages,
        float temperature = 0.2f,
        int maxTokens = 256,
        CancellationToken ct = default)
    {
        var paramsJson = JsonSerializer.SerializeToElement(
            new CompleteParams { Provider = provider, Messages = [.. messages], Temperature = temperature, MaxTokens = maxTokens },
            WorkerJsonContext.Default.CompleteParams);

        var response = await _channel.CallAsync("llm.complete", paramsJson, CompleteTimeout, ct).ConfigureAwait(false);
        response.ThrowIfError();

        return response.Deserialize(WorkerJsonContext.Default.LlmResponse)
            ?? throw new InvalidOperationException("Worker returned an empty LLM response");
    }

    public async IAsyncEnumerable<string> CompleteStreamAsync(
        LlmProviderConfig provider,
        IReadOnlyList<LlmMessage> messages,
        float temperature = 0.2f,
        int maxTokens = 256,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var paramsJson = JsonSerializer.SerializeToElement(
            new CompleteParams { Provider = provider, Messages = [.. messages], Temperature = temperature, MaxTokens = maxTokens },
            WorkerJsonContext.Default.CompleteParams);

        await foreach (var chunkJson in _channel.CallStreamingAsync("llm.completeStream", paramsJson, ct).ConfigureAwait(false))
        {
            var chunk = chunkJson.Deserialize(WorkerJsonContext.Default.LlmStreamChunk);
            if (chunk is { Delta.Length: > 0 })
                yield return chunk.Delta;
        }
    }

    public async Task<LlmStatus> GetStatusAsync(LlmProviderConfig provider, CancellationToken ct = default)
    {
        try
        {
            var paramsJson = JsonSerializer.SerializeToElement(
                new GetLlmStatusParams { Provider = provider },
                WorkerJsonContext.Default.GetLlmStatusParams);

            var response = await _channel.CallAsync("llm.getStatus", paramsJson, ct: ct).ConfigureAwait(false);
            response.ThrowIfError();

            return response.Deserialize(WorkerJsonContext.Default.LlmStatus)
                ?? throw new InvalidOperationException("Worker returned an empty status response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetStatusAsync failed");
            throw;
        }
    }
}
