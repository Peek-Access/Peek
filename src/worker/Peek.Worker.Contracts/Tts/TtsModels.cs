using System.Text.Json.Serialization;

namespace Peek.Worker.Contracts.Tts;

public sealed class TtsVoiceInfo
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("language")]
    public required string Language { get; init; }

    [JsonPropertyName("display_name")]
    public required string DisplayName { get; init; }
}

/// <summary>Worker-internal request shape; the wire shape is <c>SpeakParams</c>.</summary>
public sealed class TtsSpeakRequest
{
    public required string Text { get; init; }

    /// <summary>Piper model key (e.g. "en_US-lessac-medium"). Null = configured default voice.</summary>
    public string? VoiceId { get; init; }

    /// <summary>Speaking rate multiplier. 1.0 = normal; lower is faster, higher is slower (matches Piper's length-scale).</summary>
    public float Rate { get; init; } = 1f;

    /// <summary>When true, drops any not-yet-started queued utterance before enqueueing this one.</summary>
    public bool Interrupt { get; init; } = true;
}

public sealed class TtsSpeakResult
{
    [JsonPropertyName("audio_data")]
    public required byte[] AudioData { get; init; }

    /// <summary>Container format of <see cref="AudioData"/>, e.g. "wav".</summary>
    [JsonPropertyName("format")]
    public required string Format { get; init; }

    [JsonPropertyName("duration_ms")]
    public double DurationMs { get; init; }

    [JsonPropertyName("synthesis_ms")]
    public double SynthesisMs { get; init; }
}

public sealed class TtsStatus
{
    [JsonPropertyName("is_ready")]
    public bool IsReady { get; init; }

    [JsonPropertyName("active_voice_id")]
    public string? ActiveVoiceId { get; init; }

    [JsonPropertyName("queue_depth")]
    public int QueueDepth { get; init; }

    [JsonPropertyName("utterances_synthesized")]
    public long UtterancesSynthesized { get; init; }

    [JsonPropertyName("last_synthesis_ms")]
    public double LastSynthesisMs { get; init; }
}
