using Peek.Worker.Contracts.SystemMonitor;

namespace Peek.Ipc.Worker;

/// <summary>
/// Typed async API for querying Peek.Worker's ISystemMonitorService over the peek-worker
/// named pipe - backs the App Monitor and Process Monitor dock views. All methods throw
/// <see cref="WorkerRpcException"/> on worker-side errors (transport/protocol failures,
/// not app-launch/process-kill failures - those come back as ActionResult.Success=false).
/// </summary>
public interface ISystemMonitorClient
{
    Task<IReadOnlyList<InstalledAppDto>> EnumerateAppsAsync(CancellationToken ct = default);

    Task<ActionResult> LaunchAppAsync(string key, CancellationToken ct = default);

    Task<IReadOnlyList<ProcessDto>> EnumerateProcessesAsync(bool includeSystemProcesses, CancellationToken ct = default);

    Task<ActionResult> KillProcessAsync(int processId, CancellationToken ct = default);

    Task<ActionResult> OpenProcessFolderAsync(int processId, CancellationToken ct = default);
}
