using System.Text.Json.Serialization;

namespace Peek.Worker.Llm;

// Ollama's /api/chat wire shapes - kept internal and separate from
// Peek.Worker.Contracts.Llm.LlmMessage so Ollama-specific JSON never leaks past
// this project (§15).

internal sealed class OllamaChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required List<OllamaMessage> Messages { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }

    [JsonPropertyName("options")]
    public OllamaRequestOptions? Options { get; init; }
}

internal sealed class OllamaMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    /// <summary>Base64-encoded image(s) - only meaningful for a multimodal model (e.g. llava, llama3.2-vision).</summary>
    [JsonPropertyName("images")]
    public List<string>? Images { get; init; }
}

internal sealed class OllamaRequestOptions
{
    [JsonPropertyName("temperature")]
    public float Temperature { get; init; }

    [JsonPropertyName("num_predict")]
    public int NumPredict { get; init; }
}

internal sealed class OllamaChatResponse
{
    [JsonPropertyName("message")]
    public OllamaMessage? Message { get; init; }

    [JsonPropertyName("done")]
    public bool Done { get; init; }
}

[JsonSerializable(typeof(OllamaChatRequest))]
[JsonSerializable(typeof(OllamaChatResponse))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class OllamaJsonContext : JsonSerializerContext;
