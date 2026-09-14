using System.Text.Json.Serialization;

namespace Peek.Worker.Contracts.Screenshot;

public sealed class ScreenshotResult
{
    /// <summary>PNG-encoded image bytes.</summary>
    [JsonPropertyName("image_data")]
    public required byte[] ImageData { get; init; }

    [JsonPropertyName("width")]
    public required int Width { get; init; }

    [JsonPropertyName("height")]
    public required int Height { get; init; }
}
