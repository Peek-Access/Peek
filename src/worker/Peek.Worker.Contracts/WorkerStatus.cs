using System.Text.Json.Serialization;

namespace Peek.Worker.Contracts;

public sealed class WorkerStatus
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("uptime_secs")]
    public ulong UptimeSecs { get; init; }

    [JsonPropertyName("queries_served")]
    public ulong QueriesServed { get; init; }

    [JsonPropertyName("cache_hits")]
    public ulong CacheHits { get; init; }

    [JsonPropertyName("cache_misses")]
    public ulong CacheMisses { get; init; }

    [JsonPropertyName("state")]
    public string State { get; init; } = "Ready";
}
