namespace Peek.Worker.Contracts.SystemMonitor;

/// <summary>One entry from the OS's installed-apps registry - see ISystemMonitorService.EnumerateAppsAsync.</summary>
public sealed class InstalledAppDto
{
    public required string Key { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;

    /// <summary>Disk usage in KB as the OS reports it - null when the installer never recorded one.</summary>
    public long? EstimatedSizeKb { get; init; }

    /// <summary>False disables the launch action client-side - the worker couldn't resolve a launchable executable for this entry.</summary>
    public bool CanLaunch { get; init; }
}

/// <summary>One running process - see ISystemMonitorService.EnumerateProcessesAsync.</summary>
public sealed class ProcessDto
{
    public required int Id { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>Private bytes - the closest cheaply-available approximation of Task Manager's "memory" column.</summary>
    public long MemoryBytes { get; init; }

    public string Description { get; init; } = string.Empty;

    /// <summary>Session 0 is the services/system session on Windows - the practical heuristic for "system process", not a security boundary. Kill is refused server-side for these regardless of what the caller passes.</summary>
    public bool IsSystemProcess { get; init; }
}

/// <summary>Outcome of a launch/kill/open-folder action - a DTO stand-in for the (bool, out string?) shape RPC can't carry directly.</summary>
public sealed class ActionResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
}
