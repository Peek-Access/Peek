using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Peek.Worker.Contracts;
using Peek.Worker.Contracts.Automation;
using Peek.Worker.Contracts.Rpc;

namespace Peek.Ipc.Worker;

public sealed class AutomationClient : IAutomationClient
{
    private readonly WorkerRpcChannel _channel;
    private readonly ILogger<AutomationClient> _logger;

    public AutomationClient(WorkerRpcChannel channel, ILogger<AutomationClient> logger)
    {
        _channel = channel;
        _logger = logger;
    }

    public async Task<SemanticElement?> GetElementFromPointAsync(int x, int y, CancellationToken ct = default)
    {
        var response = await CallAsync(
            "automation.getElementFromPoint",
            new GetElementFromPointParams { X = x, Y = y },
            WorkerJsonContext.Default.GetElementFromPointParams,
            ct).ConfigureAwait(false);

        return response.Deserialize(WorkerJsonContext.Default.ElementResult)?.Element;
    }

    public async Task<SemanticElement?> GetElementFromHandleAsync(nint hwnd, CancellationToken ct = default)
    {
        var response = await CallAsync(
            "automation.getElementFromHandle",
            new GetElementFromHandleParams { Hwnd = hwnd },
            WorkerJsonContext.Default.GetElementFromHandleParams,
            ct).ConfigureAwait(false);

        return response.Deserialize(WorkerJsonContext.Default.ElementResult)?.Element;
    }

    public async Task<SemanticElement?> GetFocusedElementAsync(CancellationToken ct = default)
    {
        var response = await _channel.CallAsync("automation.getFocusedElement", ct: ct).ConfigureAwait(false);
        response.ThrowIfError();
        return response.Deserialize(WorkerJsonContext.Default.ElementResult)?.Element;
    }

    public async Task<IReadOnlyList<SemanticElement>> GetChildrenAsync(nint hwnd, int depth = 0, CancellationToken ct = default)
    {
        var response = await CallAsync(
            "automation.getChildren",
            new GetChildrenParams { Hwnd = hwnd, Depth = depth },
            WorkerJsonContext.Default.GetChildrenParams,
            ct).ConfigureAwait(false);

        var result = response.Deserialize(WorkerJsonContext.Default.ChildrenResult);
        return result?.Items ?? [];
    }

    public async Task ClearCacheAsync(CancellationToken ct = default)
    {
        var response = await _channel.CallAsync("automation.clearCache", ct: ct).ConfigureAwait(false);
        response.ThrowIfError();
    }

    public async Task<SemanticElement?> GetInspectorRootAsync(nint hwnd, CancellationToken ct = default)
    {
        var response = await CallAsync(
            "automation.inspector.getRoot",
            new InspectorGetRootParams { Hwnd = hwnd },
            WorkerJsonContext.Default.InspectorGetRootParams,
            ct).ConfigureAwait(false);

        return response.Deserialize(WorkerJsonContext.Default.ElementResult)?.Element;
    }

    public async Task<IReadOnlyList<SemanticElement>> GetInspectorChildrenAsync(string elementId, CancellationToken ct = default)
    {
        var response = await CallAsync(
            "automation.inspector.getChildren",
            new InspectorGetChildrenParams { ElementId = elementId },
            WorkerJsonContext.Default.InspectorGetChildrenParams,
            ct).ConfigureAwait(false);

        var result = response.Deserialize(WorkerJsonContext.Default.ChildrenResult);
        return result?.Items ?? [];
    }

    public async Task ResetInspectorAsync(CancellationToken ct = default)
    {
        var response = await _channel.CallAsync("automation.inspector.reset", ct: ct).ConfigureAwait(false);
        response.ThrowIfError();
    }

    public async Task<SemanticElement?> RefreshInspectorElementAsync(string elementId, CancellationToken ct = default)
    {
        var response = await CallAsync(
            "automation.inspector.refresh",
            new InspectorRefreshParams { ElementId = elementId },
            WorkerJsonContext.Default.InspectorRefreshParams,
            ct).ConfigureAwait(false);

        return response.Deserialize(WorkerJsonContext.Default.ElementResult)?.Element;
    }

    public async Task<WorkerStatus> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _channel.CallAsync("system.getStatus", ct: ct).ConfigureAwait(false);
            response.ThrowIfError();

            return response.Deserialize(WorkerJsonContext.Default.WorkerStatus)
                ?? throw new InvalidOperationException("Worker returned an empty status response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetStatusAsync failed");
            throw;
        }
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _channel.CallAsync("system.ping", ct: ct).ConfigureAwait(false);
            return response.Error is null;
        }
        catch
        {
            return false;
        }
    }

    private async Task<RpcResponse> CallAsync<TParams>(
        string method,
        TParams @params,
        JsonTypeInfo<TParams> paramsTypeInfo,
        CancellationToken ct)
    {
        var paramsJson = JsonSerializer.SerializeToElement(@params, paramsTypeInfo);
        var response = await _channel.CallAsync(method, paramsJson, ct: ct).ConfigureAwait(false);
        response.ThrowIfError();
        return response;
    }
}
