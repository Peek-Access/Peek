using Peek.Worker.Contracts.Llm;

namespace Peek.Ipc.Worker;

/// <summary>
/// Typed async API for Peek.Worker's LLM completion capability over the peek-worker
/// named pipe. The worker itself is stateless with respect to AI settings - every call
/// carries the full <see cref="LlmProviderConfig"/> (which provider, endpoint, credentials,
/// model) the client resolved from AiSettings, rather than the worker owning a single
/// startup-configured provider. All methods throw <see cref="WorkerRpcException"/> on
/// worker-side errors.
/// </summary>
public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(
        LlmProviderConfig provider,
        IReadOnlyList<LlmMessage> messages,
        float temperature = 0.2f,
        int maxTokens = 256,
        CancellationToken ct = default);

    /// <summary>Streams the completion as text deltas - see ScreenAnalysisService for why this matters (spoken narration should start as soon as the first sentence is ready, not after the whole response).</summary>
    IAsyncEnumerable<string> CompleteStreamAsync(
        LlmProviderConfig provider,
        IReadOnlyList<LlmMessage> messages,
        float temperature = 0.2f,
        int maxTokens = 256,
        CancellationToken ct = default);

    /// <summary>Checks whether one specific provider configuration is reachable - e.g. a Settings "test connection" action.</summary>
    Task<LlmStatus> GetStatusAsync(LlmProviderConfig provider, CancellationToken ct = default);
}
