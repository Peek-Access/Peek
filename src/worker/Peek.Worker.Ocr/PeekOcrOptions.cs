namespace Peek.Worker.Ocr;

public sealed class PeekOcrOptions
{
    /// <summary>Runs the text-line direction classifier before recognition (handles rotated/upside-down text).</summary>
    public bool UseDirectionClassification { get; set; } = true;

    /// <summary>How long an identical (image, region) request may be served from cache instead of re-running inference (§3/§25).</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Upper bound on cached results, evicted oldest-first.</summary>
    public int MaxCacheEntries { get; set; } = 8;
}
