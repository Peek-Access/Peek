using System.Text.Json.Serialization;

namespace Peek.Worker.Contracts.SystemMonitor;

public sealed class InstalledAppsResult
{
    [JsonPropertyName("items")]
    public List<InstalledAppDto> Items { get; init; } = [];
}

public sealed class LaunchAppParams
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;
}

public sealed class EnumerateProcessesParams
{
    [JsonPropertyName("include_system_processes")]
    public bool IncludeSystemProcesses { get; init; }
}

public sealed class ProcessesResult
{
    [JsonPropertyName("items")]
    public List<ProcessDto> Items { get; init; } = [];
}

public sealed class ProcessActionParams
{
    [JsonPropertyName("process_id")]
    public int ProcessId { get; init; }
}
