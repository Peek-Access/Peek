namespace Peek.Worker.Contracts.Llm;

/// <summary>
/// Cross-platform LLM completion capability (§15). A generic "send messages, get a
/// completion" seam - accessibility-specific prompt layering (§14: base + user
/// customization + task + UIA/OCR context) happens client-side in Peek.Core, not
/// here, so this interface stays provider-agnostic and reusable for any provider
/// (Ollama, OpenAI, Anthropic, Gemini, OpenRouter, any OpenAI-compatible custom
/// endpoint) without accessibility concerns leaking into it.
/// </summary>
public interface ILlmProvider
{
    string Name { get; }

    /// <summary>Whether this provider's wire protocol can carry an image in a message (LlmMessage.ImageDataBase64) - actual per-model vision support still depends on the model the caller picked.</summary>
    bool SupportsVision { get; }

    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);

    /// <summary>Same request shape as <see cref="CompleteAsync"/>, but yields the response incrementally as text deltas - used for the screen/window analysis narration (ScreenAnalysisService), where speaking as the model produces text matters far more than for a short element description.</summary>
    IAsyncEnumerable<string> StreamAsync(LlmRequest request, CancellationToken ct = default);

    Task<LlmStatus> GetStatusAsync(CancellationToken ct = default);
}
