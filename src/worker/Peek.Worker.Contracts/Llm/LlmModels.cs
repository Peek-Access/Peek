using System.Text.Json.Serialization;

namespace Peek.Worker.Contracts.Llm;

public sealed class LlmMessage
{
    /// <summary>"system", "user", or "assistant".</summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    /// <summary>Base64-encoded image bytes (PNG) - set for a vision request (e.g. screen/window analysis). Null for a plain text message.</summary>
    [JsonPropertyName("image_data_base64")]
    public string? ImageDataBase64 { get; init; }

    [JsonPropertyName("image_mime_type")]
    public string? ImageMimeType { get; init; }
}

/// <summary>
/// Which provider/endpoint/model/credentials a request should use. The worker is
/// stateless with respect to AI settings (§17: settings live client-side) - the client
/// resolves this from AiSettings and sends it with every request, rather than the worker
/// owning a single startup-configured provider.
/// </summary>
public sealed class LlmProviderConfig
{
    /// <summary>"ollama", "openai", "anthropic", "gemini", "openrouter", or "custom".</summary>
    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    /// <summary>Base URL. Required for "custom"; a sensible default is used for the other well-known providers when omitted.</summary>
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; init; }

    [JsonPropertyName("api_key")]
    public string? ApiKey { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }
}

/// <summary>Worker-internal request shape; the wire shape is <c>CompleteParams</c>.</summary>
public sealed class LlmRequest
{
    public required LlmProviderConfig Provider { get; init; }

    public required IReadOnlyList<LlmMessage> Messages { get; init; }

    public float Temperature { get; init; } = 0.2f;

    public int MaxTokens { get; init; } = 256;
}

public sealed class LlmResponse
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("duration_ms")]
    public double DurationMs { get; init; }
}

/// <summary>One streamed text delta - the wire shape yielded by <c>llm.completeStream</c>, one per chunk.</summary>
public sealed class LlmStreamChunk
{
    [JsonPropertyName("delta")]
    public required string Delta { get; init; }
}

public sealed class LlmStatus
{
    [JsonPropertyName("is_ready")]
    public bool IsReady { get; init; }

    [JsonPropertyName("provider_name")]
    public string? ProviderName { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("requests_performed")]
    public long RequestsPerformed { get; init; }

    [JsonPropertyName("last_request_ms")]
    public double LastRequestMs { get; init; }
}
