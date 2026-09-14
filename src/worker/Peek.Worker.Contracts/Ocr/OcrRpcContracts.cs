using System.Text.Json.Serialization;

namespace Peek.Worker.Contracts.Ocr;

public sealed class RecognizeParams
{
    [JsonPropertyName("image_data")]
    public required byte[] ImageData { get; init; }

    [JsonPropertyName("region")]
    public OcrRegion? Region { get; init; }

    [JsonPropertyName("language")]
    public string? Language { get; init; }
}
