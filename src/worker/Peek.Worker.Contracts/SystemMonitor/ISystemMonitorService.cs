namespace Peek.Worker.Contracts.SystemMonitor;

/// <summary>
/// Installed-apps and running-processes introspection/control for the App Monitor and
/// Process Monitor dock views - kept in the worker process alongside UI Automation rather
/// than the client, for the same reason automation is: OS-facing enumeration and process
/// control belong in one place, not scattered across client and worker.
/// </summary>
public interface ISystemMonitorService
{
    Task<IReadOnlyList<InstalledAppDto>> EnumerateAppsAsync(CancellationToken ct = default);

    Task<ActionResult> LaunchAppAsync(string key, CancellationToken ct = default);

    Task<IReadOnlyList<ProcessDto>> EnumerateProcessesAsync(bool includeSystemProcesses, CancellationToken ct = default);

    /// <summary>Refuses (Success=false) for a system process (IsSystemProcess) regardless of caller intent.</summary>
    Task<ActionResult> KillProcessAsync(int processId, CancellationToken ct = default);

    Task<ActionResult> OpenProcessFolderAsync(int processId, CancellationToken ct = default);
}
