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

/// <summary>
/// A cheap, small stand-in for a full screenshot - the fingerprint an
/// Peek.Worker.Screenshot.IImageSimilarityAlgorithm produces, good only for "has this window's
/// content changed since last time" comparisons, never for display or OCR itself.
/// </summary>
public sealed class ScreenshotFingerprintResult
{
    [JsonPropertyName("fingerprint")]
    public required byte[] Fingerprint { get; init; }
}
