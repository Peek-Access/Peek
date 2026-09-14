using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Peek.Worker.Contracts.Rpc;
using Peek.Worker.Contracts.SystemMonitor;

namespace Peek.Ipc.Worker;

public sealed class SystemMonitorClient : ISystemMonitorClient
{
    // apps.enumerate (registry walk) and processes.enumerate (Process.GetProcesses() + a
    // MainModule access per process) are one-shot list loads triggered by opening a page or
    // hitting Refresh, not latency-sensitive like element/hover queries - the channel's
    // default RPC timeout (WorkerConnectionOptions.RpcTimeout, 2s) is tuned for the latter
    // and is routinely too short for these on a machine with a few hundred processes.
    private static readonly TimeSpan EnumerationTimeout = TimeSpan.FromSeconds(15);

    private readonly WorkerRpcChannel _channel;

    public SystemMonitorClient(WorkerRpcChannel channel) => _channel = channel;

    public async Task<IReadOnlyList<InstalledAppDto>> EnumerateAppsAsync(CancellationToken ct = default)
    {
        var response = await _channel.CallAsync("apps.enumerate", timeout: EnumerationTimeout, ct: ct).ConfigureAwait(false);
        response.ThrowIfError();
        return response.Deserialize(WorkerJsonContext.Default.InstalledAppsResult)?.Items ?? [];
    }

    public async Task<ActionResult> LaunchAppAsync(string key, CancellationToken ct = default)
    {
        var response = await CallAsync(
            "apps.launch", new LaunchAppParams { Key = key }, WorkerJsonContext.Default.LaunchAppParams, ct)
            .ConfigureAwait(false);

        return response.Deserialize(WorkerJsonContext.Default.ActionResult)
            ?? new ActionResult { Success = false, Error = "Empty response from worker" };
    }

    public async Task<IReadOnlyList<ProcessDto>> EnumerateProcessesAsync(bool includeSystemProcesses, CancellationToken ct = default)
    {
        var response = await CallAsync(
            "processes.enumerate",
            new EnumerateProcessesParams { IncludeSystemProcesses = includeSystemProcesses },
            WorkerJsonContext.Default.EnumerateProcessesParams,
            EnumerationTimeout,
            ct).ConfigureAwait(false);

        return response.Deserialize(WorkerJsonContext.Default.ProcessesResult)?.Items ?? [];
    }

    public async Task<ActionResult> KillProcessAsync(int processId, CancellationToken ct = default)
    {
        var response = await CallAsync(
            "processes.kill", new ProcessActionParams { ProcessId = processId }, WorkerJsonContext.Default.ProcessActionParams, ct)
            .ConfigureAwait(false);

        return response.Deserialize(WorkerJsonContext.Default.ActionResult)
            ?? new ActionResult { Success = false, Error = "Empty response from worker" };
    }

    public async Task<ActionResult> OpenProcessFolderAsync(int processId, CancellationToken ct = default)
    {
        var response = await CallAsync(
            "processes.openFolder", new ProcessActionParams { ProcessId = processId }, WorkerJsonContext.Default.ProcessActionParams, ct)
            .ConfigureAwait(false);

        return response.Deserialize(WorkerJsonContext.Default.ActionResult)
            ?? new ActionResult { Success = false, Error = "Empty response from worker" };
    }

    private async Task<RpcResponse> CallAsync<TParams>(
        string method, TParams @params, JsonTypeInfo<TParams> paramsTypeInfo, CancellationToken ct) =>
        await CallAsync(method, @params, paramsTypeInfo, timeout: null, ct).ConfigureAwait(false);

    private async Task<RpcResponse> CallAsync<TParams>(
        string method, TParams @params, JsonTypeInfo<TParams> paramsTypeInfo, TimeSpan? timeout, CancellationToken ct)
    {
        var paramsJson = JsonSerializer.SerializeToElement(@params, paramsTypeInfo);
        var response = await _channel.CallAsync(method, paramsJson, timeout: timeout, ct: ct).ConfigureAwait(false);
        response.ThrowIfError();
        return response;
    }
}
