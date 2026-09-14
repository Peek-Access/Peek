using System.Text.Json.Serialization;

namespace Peek.Worker.Contracts.Tts;

public sealed class SpeakParams
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("voice_id")]
    public string? VoiceId { get; init; }

    [JsonPropertyName("rate")]
    public float? Rate { get; init; }

    [JsonPropertyName("interrupt")]
    public bool Interrupt { get; init; } = true;
}

public sealed class VoicesResult
{
    [JsonPropertyName("voices")]
    public List<TtsVoiceInfo> Voices { get; init; } = [];
}
