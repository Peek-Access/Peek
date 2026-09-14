using System.Text.Json.Serialization;

namespace Peek.Worker.Contracts.Llm;

/// <summary>Wire shape for both "llm.complete" and "llm.completeStream" - same fields, the method name alone decides single-shot vs. streamed delivery.</summary>
public sealed class CompleteParams
{
    [JsonPropertyName("provider")]
    public required LlmProviderConfig Provider { get; init; }

    [JsonPropertyName("messages")]
    public required List<LlmMessage> Messages { get; init; }

    [JsonPropertyName("temperature")]
    public float? Temperature { get; init; }

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }
}

/// <summary>Wire shape for "llm.getStatus" - e.g. a Settings page "test connection" action against one specific provider configuration.</summary>
public sealed class GetLlmStatusParams
{
    [JsonPropertyName("provider")]
    public required LlmProviderConfig Provider { get; init; }
}
