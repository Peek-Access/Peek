using System.Text.Json.Serialization;

namespace Peek.Worker.Contracts.Ocr;

/// <summary>Pixel-space sub-rectangle of the supplied image to run OCR on, rather than the whole thing.</summary>
public sealed class OcrRegion
{
    [JsonPropertyName("x")]
    public required int X { get; init; }

    [JsonPropertyName("y")]
    public required int Y { get; init; }

    [JsonPropertyName("width")]
    public required int Width { get; init; }

    [JsonPropertyName("height")]
    public required int Height { get; init; }
}

/// <summary>Worker-internal request shape; the wire shape is <c>RecognizeParams</c>.</summary>
public sealed class OcrRequest
{
    /// <summary>Encoded image bytes (PNG) - not a raw pixel buffer, to keep RPC payloads small over the wire.</summary>
    public required byte[] ImageData { get; init; }

    public OcrRegion? Region { get; init; }

    /// <summary>Reserved for future multi-model/multi-language selection; unused by the current single-bundle implementation.</summary>
    public string? Language { get; init; }
}

public readonly record struct OcrQuad(
    float X1, float Y1, float X2, float Y2, float X3, float Y3, float X4, float Y4);

public sealed class OcrLine
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    /// <summary>
    /// The recognizer's raw per-line score. Not a normalized 0-1 probability -
    /// SimdPaddleOCR's PaddleOcrRecognitionResult.Score can exceed 1 (observed
    /// ~30-45 for confidently-recognized short lines) - useful for relative
    /// ranking between lines, not as a percentage.
    /// </summary>
    [JsonPropertyName("confidence")]
    public required float Confidence { get; init; }

    [JsonPropertyName("box")]
    public required OcrQuad Box { get; init; }
}

public sealed class OcrResult
{
    [JsonPropertyName("lines")]
    public required IReadOnlyList<OcrLine> Lines { get; init; }

    /// <summary>All recognized lines joined with newlines, for callers that just want the text.</summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("from_cache")]
    public bool FromCache { get; init; }

    [JsonPropertyName("recognition_ms")]
    public double RecognitionMs { get; init; }
}

public sealed class OcrStatus
{
    [JsonPropertyName("is_ready")]
    public bool IsReady { get; init; }

    [JsonPropertyName("model_name")]
    public string? ModelName { get; init; }

    [JsonPropertyName("recognitions_performed")]
    public long RecognitionsPerformed { get; init; }

    [JsonPropertyName("cache_hits")]
    public long CacheHits { get; init; }

    [JsonPropertyName("last_recognition_ms")]
    public double LastRecognitionMs { get; init; }
}
