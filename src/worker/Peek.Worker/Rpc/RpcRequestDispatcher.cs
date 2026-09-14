using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Peek.Worker.Contracts;
using Peek.Worker.Contracts.Automation;
using Peek.Worker.Contracts.Llm;
using Peek.Worker.Contracts.Ocr;
using Peek.Worker.Contracts.Rpc;
using Peek.Worker.Contracts.Screenshot;
using Peek.Worker.Contracts.SystemMonitor;
using Peek.Worker.Contracts.Tts;
using Peek.Worker.Llm;

namespace Peek.Worker.Rpc;

public sealed class RpcRequestDispatcher
{
    private readonly IAutomationService _automation;
    private readonly ITtsService _tts;
    private readonly IOcrService _ocr;
    private readonly IScreenshotService _screenshot;
    private readonly ILlmProviderFactory _llmProviderFactory;
    private readonly ISystemMonitorService _systemMonitor;
    private readonly ILogger<RpcRequestDispatcher> _logger;
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;
    private long _queriesServed;

    public RpcRequestDispatcher(
        IAutomationService automation, ITtsService tts, IOcrService ocr, IScreenshotService screenshot,
        ILlmProviderFactory llmProviderFactory, ISystemMonitorService systemMonitor, ILogger<RpcRequestDispatcher> logger)
    {
        _automation = automation;
        _tts = tts;
        _ocr = ocr;
        _screenshot = screenshot;
        _llmProviderFactory = llmProviderFactory;
        _systemMonitor = systemMonitor;
        _logger = logger;
    }

    /// <summary>
    /// <paramref name="emitAsync"/> lets a streaming method (llm.completeStream) write
    /// several response lines sharing this request's Id as it produces them - see
    /// RpcResponse.Done and WorkerPipeServer, which supplies it bound to the connection's
    /// own serialized writer. Null for the (overwhelming majority) non-streaming call path -
    /// every existing method ignores it entirely and behaves exactly as before.
    /// </summary>
    public async Task<string> DispatchAsync(string requestLine, CancellationToken ct, Func<string, CancellationToken, Task>? emitAsync = null)
    {
        RpcRequest? request;
        try
        {
            request = JsonSerializer.Deserialize(requestLine, WorkerJsonContext.Default.RpcRequest);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse request line");
            return Serialize(RpcResponse.Failure(0, RpcErrorCodes.ParseError, "Invalid JSON"));
        }

        if (request is null)
            return Serialize(RpcResponse.Failure(0, RpcErrorCodes.ParseError, "Empty request"));

        Interlocked.Increment(ref _queriesServed);

        try
        {
            var result = await RouteAsync(request, emitAsync, ct).ConfigureAwait(false);
            return Serialize(RpcResponse.Success(request.Id, result));
        }
        catch (RpcMethodException ex)
        {
            return Serialize(RpcResponse.Failure(request.Id, ex.Code, ex.Message));
        }
        catch (OperationCanceledException)
        {
            return Serialize(RpcResponse.Failure(request.Id, RpcErrorCodes.Timeout, "Cancelled"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Method '{Method}' failed", request.Method);
            return Serialize(RpcResponse.Failure(request.Id, RpcErrorCodes.InternalError, ex.Message));
        }
    }

    /// <summary>
    /// Dispatches by method-name prefix to one of the per-service Route*Async methods
    /// below. This is a readability split, not a plugin-registration system: Program.cs
    /// already wires every worker service's concrete implementation by hand
    /// (builder.Services.AddSingleton&lt;IAutomationService, WindowsAutomationService&gt;()
    /// etc.), so a new plugin needs that edit regardless of how RPC routing is organized
    /// here - a route-registration abstraction on top of this method alone wouldn't
    /// actually remove the "touch a central file to add a plugin" requirement the rest of
    /// the architecture already has, just relocate part of it.
    /// </summary>
    private Task<JsonElement> RouteAsync(RpcRequest request, Func<string, CancellationToken, Task>? emitAsync, CancellationToken ct)
    {
        var dotIndex = request.Method.IndexOf('.');
        var prefix = dotIndex >= 0 ? request.Method[..dotIndex] : request.Method;
        return prefix switch
        {
            "automation" => RouteAutomationAsync(request, ct),
            "tts" => RouteTtsAsync(request, ct),
            "ocr" => RouteOcrAsync(request, ct),
            "screenshot" => RouteScreenshotAsync(request, ct),
            "llm" => RouteLlmAsync(request, emitAsync, ct),
            "apps" or "processes" => RouteSystemMonitorAsync(request, ct),
            "system" => RouteSystemAsync(request, ct),
            _ => throw new RpcMethodException(RpcErrorCodes.MethodNotFound, $"Unknown method '{request.Method}'"),
        };
    }

    private async Task<JsonElement> RouteAutomationAsync(RpcRequest request, CancellationToken ct)
    {
        switch (request.Method)
        {
            case "automation.getElementFromPoint":
            {
                var p = RequireParams(request, WorkerJsonContext.Default.GetElementFromPointParams);
                var element = await _automation.GetElementFromPointAsync(p.X, p.Y, ct).ConfigureAwait(false);
                return ToElement(new ElementResult { Element = element }, WorkerJsonContext.Default.ElementResult);
            }
            case "automation.getElementFromHandle":
            {
                var p = RequireParams(request, WorkerJsonContext.Default.GetElementFromHandleParams);
                var element = await _automation.GetElementFromHandleAsync(p.Hwnd, ct).ConfigureAwait(false);
                return ToElement(new ElementResult { Element = element }, WorkerJsonContext.Default.ElementResult);
            }
            case "automation.getFocusedElement":
            {
                var element = await _automation.GetFocusedElementAsync(ct).ConfigureAwait(false);
                return ToElement(new ElementResult { Element = element }, WorkerJsonContext.Default.ElementResult);
            }
            case "automation.getChildren":
            {
                var p = RequireParams(request, WorkerJsonContext.Default.GetChildrenParams);
                var children = await _automation.GetChildrenAsync(p.Hwnd, p.Depth ?? 0, ct).ConfigureAwait(false);
                return ToElement(new ChildrenResult { Items = [.. children] }, WorkerJsonContext.Default.ChildrenResult);
            }
            case "automation.clearCache":
                await _automation.ClearCacheAsync(ct).ConfigureAwait(false);
                return ToElement(new AckResult { Ok = true }, WorkerJsonContext.Default.AckResult);
            case "automation.inspector.getRoot":
            {
                var p = RequireParams(request, WorkerJsonContext.Default.InspectorGetRootParams);
                var element = await _automation.GetInspectorRootAsync(p.Hwnd, ct).ConfigureAwait(false);
                return ToElement(new ElementResult { Element = element }, WorkerJsonContext.Default.ElementResult);
            }
            case "automation.inspector.getChildren":
            {
                var p = RequireParams(request, WorkerJsonContext.Default.InspectorGetChildrenParams);
                var children = await _automation.GetInspectorChildrenAsync(p.ElementId, ct).ConfigureAwait(false);
                return ToElement(new ChildrenResult { Items = [.. children] }, WorkerJsonContext.Default.ChildrenResult);
            }
            case "automation.inspector.reset":
                await _automation.ResetInspectorAsync(ct).ConfigureAwait(false);
                return ToElement(new AckResult { Ok = true }, WorkerJsonContext.Default.AckResult);
            case "automation.inspector.refresh":
            {
                var p = RequireParams(request, WorkerJsonContext.Default.InspectorRefreshParams);
                var element = await _automation.RefreshInspectorElementAsync(p.ElementId, ct).ConfigureAwait(false);
                return ToElement(new ElementResult { Element = element }, WorkerJsonContext.Default.ElementResult);
            }
            default:
                throw new RpcMethodException(RpcErrorCodes.MethodNotFound, $"Unknown method '{request.Method}'");
        }
    }

    private async Task<JsonElement> RouteTtsAsync(RpcRequest request, CancellationToken ct)
    {
        switch (request.Method)
        {
            case "tts.getVoices":
            {
                var voices = await _tts.GetVoicesAsync(ct).ConfigureAwait(false);
                return ToElement(new VoicesResult { Voices = [.. voices] }, WorkerJsonContext.Default.VoicesResult);
            }
            case "tts.speak":
            {
                var p = RequireParams(request, WorkerJsonContext.Default.SpeakParams);
                var result = await _tts.SpeakAsync(new TtsSpeakRequest
                {
                    Text = p.Text,
                    VoiceId = p.VoiceId,
                    Rate = p.Rate ?? 1f,
                    Interrupt = p.Interrupt,
                }, ct).ConfigureAwait(false);
                return ToElement(result, WorkerJsonContext.Default.TtsSpeakResult);
            }
            case "tts.stop":
                await _tts.StopAsync(ct).ConfigureAwait(false);
                return ToElement(new AckResult { Ok = true }, WorkerJsonContext.Default.AckResult);
            case "tts.getStatus":
                return ToElement(await _tts.GetStatusAsync(ct).ConfigureAwait(false), WorkerJsonContext.Default.TtsStatus);
            default:
                throw new RpcMethodException(RpcErrorCodes.MethodNotFound, $"Unknown method '{request.Method}'");
        }
    }

    private async Task<JsonElement> RouteOcrAsync(RpcRequest request, CancellationToken ct)
    {
        switch (request.Method)
        {
            case "ocr.recognize":
            {
                var p = RequireParams(request, WorkerJsonContext.Default.RecognizeParams);
                var result = await _ocr.RecognizeAsync(new OcrRequest
                {
                    ImageData = p.ImageData,
                    Region = p.Region,
                    Language = p.Language,
                }, ct).ConfigureAwait(false);
                return ToElement(result, WorkerJsonContext.Default.OcrResult);
            }
            case "ocr.getStatus":
                return ToElement(await _ocr.GetStatusAsync(ct).ConfigureAwait(false), WorkerJsonContext.Default.OcrStatus);
            default:
                throw new RpcMethodException(RpcErrorCodes.MethodNotFound, $"Unknown method '{request.Method}'");
        }
    }

    private async Task<JsonElement> RouteScreenshotAsync(RpcRequest request, CancellationToken ct)
    {
        switch (request.Method)
        {
            case "screenshot.captureDesktop":
            {
                var result = await _screenshot.CaptureDesktopAsync(ct).ConfigureAwait(false);
                return ToElement(result, WorkerJsonContext.Default.ScreenshotResult);
            }
            case "screenshot.captureWindow":
            {
                var p = RequireParams(request, WorkerJsonContext.Default.CaptureWindowParams);
                var result = await _screenshot.CaptureWindowAsync(p.Hwnd, ct).ConfigureAwait(false);
                return ToElement(result, WorkerJsonContext.Default.ScreenshotResult);
            }
            default:
                throw new RpcMethodException(RpcErrorCodes.MethodNotFound, $"Unknown method '{request.Method}'");
        }
    }

    private async Task<JsonElement> RouteLlmAsync(RpcRequest request, Func<string, CancellationToken, Task>? emitAsync, CancellationToken ct)
    {
        switch (request.Method)
        {
            case "llm.complete":
            {
                var p = RequireParams(request, WorkerJsonContext.Default.CompleteParams);
                var provider = _llmProviderFactory.GetProvider(p.Provider);
                var result = await provider.CompleteAsync(new LlmRequest
                {
                    Provider = p.Provider,
                    Messages = p.Messages,
                    Temperature = p.Temperature ?? 0.2f,
                    MaxTokens = p.MaxTokens ?? 256,
                }, ct).ConfigureAwait(false);
                return ToElement(result, WorkerJsonContext.Default.LlmResponse);
            }
            case "llm.completeStream":
            {
                if (emitAsync is null)
                    throw new RpcMethodException(RpcErrorCodes.InternalError, "Streaming transport not available for this connection");

                var p = RequireParams(request, WorkerJsonContext.Default.CompleteParams);
                var provider = _llmProviderFactory.GetProvider(p.Provider);
                var llmRequest = new LlmRequest
                {
                    Provider = p.Provider,
                    Messages = p.Messages,
                    Temperature = p.Temperature ?? 0.2f,
                    MaxTokens = p.MaxTokens ?? 256,
                };

                await foreach (var delta in provider.StreamAsync(llmRequest, ct).ConfigureAwait(false))
                {
                    var chunkElement = ToElement(new LlmStreamChunk { Delta = delta }, WorkerJsonContext.Default.LlmStreamChunk);
                    var chunkJson = Serialize(RpcResponse.StreamChunk(request.Id, chunkElement, done: false));
                    await emitAsync(chunkJson, ct).ConfigureAwait(false);
                }

                // The final line: an ordinary (Done: null) response, exactly like every
                // non-streaming method's return value - WorkerRpcChannel.CallStreamingAsync
                // treats "Done != false" as end-of-stream, so this needs no special marker.
                return ToElement(new LlmStreamChunk { Delta = "" }, WorkerJsonContext.Default.LlmStreamChunk);
            }
            case "llm.getStatus":
            {
                var p = RequireParams(request, WorkerJsonContext.Default.GetLlmStatusParams);
                var provider = _llmProviderFactory.GetProvider(p.Provider);
                return ToElement(await provider.GetStatusAsync(ct).ConfigureAwait(false), WorkerJsonContext.Default.LlmStatus);
            }
            default:
                throw new RpcMethodException(RpcErrorCodes.MethodNotFound, $"Unknown method '{request.Method}'");
        }
    }

    private async Task<JsonElement> RouteSystemMonitorAsync(RpcRequest request, CancellationToken ct)
    {
        switch (request.Method)
        {
            case "apps.enumerate":
            {
                var apps = await _systemMonitor.EnumerateAppsAsync(ct).ConfigureAwait(false);
                return ToElement(new InstalledAppsResult { Items = [.. apps] }, WorkerJsonContext.Default.InstalledAppsResult);
            }
            case "apps.launch":
            {
                var p = RequireParams(request, WorkerJsonContext.Default.LaunchAppParams);
                var result = await _systemMonitor.LaunchAppAsync(p.Key, ct).ConfigureAwait(false);
                return ToElement(result, WorkerJsonContext.Default.ActionResult);
            }
            case "processes.enumerate":
            {
                var p = RequireParams(request, WorkerJsonContext.Default.EnumerateProcessesParams);
                var processes = await _systemMonitor.EnumerateProcessesAsync(p.IncludeSystemProcesses, ct).ConfigureAwait(false);
                return ToElement(new ProcessesResult { Items = [.. processes] }, WorkerJsonContext.Default.ProcessesResult);
            }
            case "processes.kill":
            {
                var p = RequireParams(request, WorkerJsonContext.Default.ProcessActionParams);
                var result = await _systemMonitor.KillProcessAsync(p.ProcessId, ct).ConfigureAwait(false);
                return ToElement(result, WorkerJsonContext.Default.ActionResult);
            }
            case "processes.openFolder":
            {
                var p = RequireParams(request, WorkerJsonContext.Default.ProcessActionParams);
                var result = await _systemMonitor.OpenProcessFolderAsync(p.ProcessId, ct).ConfigureAwait(false);
                return ToElement(result, WorkerJsonContext.Default.ActionResult);
            }
            default:
                throw new RpcMethodException(RpcErrorCodes.MethodNotFound, $"Unknown method '{request.Method}'");
        }
    }

    private Task<JsonElement> RouteSystemAsync(RpcRequest request, CancellationToken ct)
    {
        switch (request.Method)
        {
            case "system.getStatus":
                return Task.FromResult(ToElement(BuildStatus(), WorkerJsonContext.Default.WorkerStatus));
            case "system.ping":
                return Task.FromResult(ToElement(new AckResult { Ok = true }, WorkerJsonContext.Default.AckResult));
            default:
                throw new RpcMethodException(RpcErrorCodes.MethodNotFound, $"Unknown method '{request.Method}'");
        }
    }

    private WorkerStatus BuildStatus()
    {
        var diagnostics = _automation.GetDiagnostics();
        return new WorkerStatus
        {
            Version = typeof(RpcRequestDispatcher).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            UptimeSecs = (ulong)Math.Max(0, (DateTime.UtcNow - _startedAtUtc).TotalSeconds),
            QueriesServed = (ulong)Interlocked.Read(ref _queriesServed),
            CacheHits = (ulong)Math.Max(0, diagnostics.CacheHits),
            CacheMisses = (ulong)Math.Max(0, diagnostics.CacheMisses),
            State = "Ready",
        };
    }

    private static T RequireParams<T>(RpcRequest request, JsonTypeInfo<T> typeInfo)
    {
        if (request.Params is not { } element || element.ValueKind == JsonValueKind.Undefined)
            throw new RpcMethodException(RpcErrorCodes.InvalidParams, $"Method '{request.Method}' requires params");

        return JsonSerializer.Deserialize(element, typeInfo)
            ?? throw new RpcMethodException(RpcErrorCodes.InvalidParams, $"Method '{request.Method}' has invalid params");
    }

    private static JsonElement ToElement<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }

    private static string Serialize(RpcResponse response) =>
        JsonSerializer.Serialize(response, WorkerJsonContext.Default.RpcResponse);
}
