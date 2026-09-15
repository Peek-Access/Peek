using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Peek.Worker.Contracts.Rpc;
using Peek.Ipc.Transport;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace Peek.Ipc.Worker;

public sealed class WorkerRpcChannel : IDisposable
{
    private readonly IPipeTransport _transport;
    private readonly ILogger<WorkerRpcChannel> _logger;
    private readonly TimeSpan _defaultTimeout;

    private int _nextId;

    private readonly ConcurrentDictionary<int, PendingWorkerCall> _pending = new();
    private readonly Signal<RpcResponse> _responseSubject = new();
    private readonly IDisposable _lineSubscription;
    private WorkerRpcChannel? _replacement;
    private int _retiring;
    private int _disposed;
    private readonly TaskCompletionSource<WorkerRpcChannel> _replacementReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WorkerRpcChannel(
        IPipeTransport transport,
        ILogger<WorkerRpcChannel> logger,
        TimeSpan? defaultTimeout = null)
    {
        _transport = transport;
        _logger = logger;
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(20);

        _lineSubscription = _transport.ReceivedLines.Subscribe(OnLineReceived, OnTransportError);
    }

    public IObservable<RpcResponse> Responses => _responseSubject.AsObservable();

    public async Task<RpcResponse> CallAsync(
        string method,
        JsonElement? @params = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var replacement = await GetReplacementAsync(ct).ConfigureAwait(false);
        if (replacement is not null)
            return await replacement.CallAsync(method, @params, timeout, ct).ConfigureAwait(false);

        var id = Interlocked.Increment(ref _nextId);

        var request = new RpcRequest
        {
            Id = id,
            Method = method,
            Params = @params,
        };

        var effectiveTimeout = timeout ?? _defaultTimeout;
        var pending = new PendingWorkerCall(id, effectiveTimeout, ct);

        if (!_pending.TryAdd(id, pending))
            throw new InvalidOperationException($"Duplicate request ID {id}");

        string json;
        try
        {
            json = JsonSerializer.Serialize(request, WorkerJsonContext.Default.RpcRequest);
        }
        catch (Exception ex)
        {
            _pending.TryRemove(id, out _);
            pending.Dispose();
            throw new InvalidOperationException("Failed to serialize request", ex);
        }

        _logger.LogTrace("-> [{Id}] {Method}", id, method);

        try
        {
            try
            {
                await _transport.SendLineAsync(json, ct).ConfigureAwait(false);
            }
            catch (Exception) when (Volatile.Read(ref _retiring) != 0)
            {
                var replacementChannel = await _replacementReady.Task.WaitAsync(ct).ConfigureAwait(false);
                return await replacementChannel.CallAsync(method, @params, timeout, ct).ConfigureAwait(false);
            }
            var completed = await Task.WhenAny(pending.Task, _replacementReady.Task).ConfigureAwait(false);
            if (completed == _replacementReady.Task)
            {
                var replacementChannel = await _replacementReady.Task.ConfigureAwait(false);
                return await replacementChannel.CallAsync(method, @params, timeout, ct).ConfigureAwait(false);
            }
            return await pending.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
            pending.Dispose();
        }
    }

    /// <summary>
    /// Like <see cref="CallAsync"/>, but for a method that replies with several
    /// RpcResponse lines sharing one request Id instead of exactly one (see
    /// RpcResponse.Done) - used for llm.completeStream so a screen/window analysis can be
    /// spoken sentence-by-sentence as the model produces it, instead of waiting for the
    /// whole completion. Deliberately bypasses the _pending/PendingWorkerCall machinery
    /// entirely: that's built around exactly-one-response-per-id, and streaming's
    /// many-responses-per-id shape doesn't fit it - subscribing directly to the broadcast
    /// Responses observable (already fired unconditionally for every line in
    /// OnLineReceived) is simpler than bolting a second completion mode onto it.
    /// </summary>
    public async IAsyncEnumerable<JsonElement> CallStreamingAsync(
        string method,
        JsonElement? @params = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var replacement = await GetReplacementAsync(ct).ConfigureAwait(false);
        if (replacement is not null)
        {
            await foreach (var result in replacement.CallStreamingAsync(method, @params, ct).ConfigureAwait(false))
                yield return result;
            yield break;
        }
        if (Volatile.Read(ref _retiring) != 0 || Volatile.Read(ref _disposed) != 0)
            throw new IOException("Worker RPC channel was replaced while the worker was recovering.");

        var id = Interlocked.Increment(ref _nextId);
        var channel = Channel.CreateUnbounded<RpcResponse>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        using var subscription = Responses.Subscribe(r =>
        {
            if (r.Id == id) channel.Writer.TryWrite(r);
        });

        var request = new RpcRequest { Id = id, Method = method, Params = @params };
        string json;
        try
        {
            json = JsonSerializer.Serialize(request, WorkerJsonContext.Default.RpcRequest);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to serialize streaming request", ex);
        }

        _logger.LogTrace("-> [{Id}] {Method} (streaming)", id, method);
        await _transport.SendLineAsync(json, ct).ConfigureAwait(false);

        await foreach (var response in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (response.Error is not null)
                throw new WorkerRpcException(response.Error);

            if (response.Result is { } result)
                yield return result;

            if (response.Done != false)
                yield break;
        }
    }

    private void OnLineReceived(string line)
    {
        RpcResponse response;
        try
        {
            response = JsonSerializer.Deserialize(line, WorkerJsonContext.Default.RpcResponse)
                ?? throw new JsonException("Deserialized to null");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse worker response line: {Line}",
                line.Length > 120 ? line[..120] + "..." : line);
            return;
        }

        _responseSubject.OnNext(response);

        if (_pending.TryGetValue(response.Id, out var pending))
        {
            _logger.LogTrace("<- [{Id}] success={Success}", response.Id, response.Error is null);

            if (response.Error is null)
                pending.Complete(response);
            else
                pending.Fail(new WorkerRpcException(response.Error));
        }
        else if (response.Done == false)
        {
            // Expected, not an anomaly: a streaming chunk (see CallStreamingAsync) is
            // deliberately never added to _pending - it's consumed via the Responses
            // broadcast subscription instead. Every chunk of every streaming response
            // (llm.completeStream can emit dozens per AI analysis) would otherwise log
            // here, making a working feature look like it's failing repeatedly.
        }
        else
        {
            // Routine, not just a protocol anomaly: CallAsync removes an ID from _pending
            // the instant its Task completes, including on cancellation - and
            // TrackMouseElement's Select+Switch cancels the previous in-flight
            // automation.getElementFromPoint call on every new mouse position (see
            // WorkerClientExtensions), so during normal fast mouse movement the worker's
            // answer for an already-abandoned call routinely arrives after its ID is gone.
            // That response is genuinely irrelevant now, not a bug - Trace, not Debug, so
            // it doesn't read as an error during ordinary use.
            _logger.LogTrace("Received response for unknown/expired ID {Id}", response.Id);
        }
    }

    private void OnTransportError(Exception ex)
    {
        _logger.LogError(ex, "Worker transport error - failing all pending calls");

        foreach (var (_, pending) in _pending)
            pending.Fail(new IOException("Transport faulted", ex));

        _pending.Clear();
    }

    internal void Retire()
    {
        if (Interlocked.Exchange(ref _retiring, 1) != 0) return;
    }

    internal void SetReplacement(WorkerRpcChannel replacement)
    {
        Interlocked.Exchange(ref _replacement, replacement);
        _replacementReady.TrySetResult(replacement);
    }

    private async Task<WorkerRpcChannel?> GetReplacementAsync(CancellationToken ct)
    {
        var replacement = Volatile.Read(ref _replacement);
        if (replacement is not null) return replacement;
        if (Volatile.Read(ref _retiring) == 0 && Volatile.Read(ref _disposed) == 0) return null;
        if (Volatile.Read(ref _disposed) != 0)
            throw new IOException("Worker RPC channel was disposed.");
        return await _replacementReady.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _replacementReady.TrySetException(new IOException("Worker RPC channel was disposed."));
        _lineSubscription.Dispose();
        _responseSubject.OnCompleted();
        _responseSubject.Dispose();

        foreach (var (_, pending) in _pending)
        {
            pending.Fail(new ObjectDisposedException(nameof(WorkerRpcChannel)));
            pending.Dispose();
        }
        _pending.Clear();
    }
}

internal sealed class PendingWorkerCall : IDisposable
{
    private readonly TaskCompletionSource<RpcResponse> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly CancellationTokenSource _timeoutCts;
    private readonly CancellationTokenRegistration _ctReg;
    private readonly CancellationTokenRegistration _timeoutReg;

    internal Task<RpcResponse> Task => _tcs.Task;

    public PendingWorkerCall(int id, TimeSpan timeout, CancellationToken callerCt)
    {
        _timeoutCts = new CancellationTokenSource(timeout);

        _timeoutReg = _timeoutCts.Token.Register(() =>
            _tcs.TrySetException(new TimeoutException($"Worker RPC call ID {id} timed out after {timeout}")));

        _ctReg = callerCt.Register(() => _tcs.TrySetCanceled(callerCt));

        // The real result still propagates to whoever awaits Task; this only marks the
        // exception observed so the finalizer can't rethrow it as an unobserved task
        // exception. That fires in a narrow but real window: CallAsync throws out of
        // SendLineAsync (broken pipe) and so never reaches `await pending.Task`, while
        // OnTransportError/Dispose concurrently faults this same call - without this, that
        // surfaces as a Critical "Unobserved task exception ... ObjectDisposedException:
        // WorkerRpcChannel" plus a crash report, every time the worker dies.
        _ = _tcs.Task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public void Complete(RpcResponse response) => _tcs.TrySetResult(response);

    public void Fail(Exception ex) => _tcs.TrySetException(ex);

    public void Dispose()
    {
        _ctReg.Dispose();
        _timeoutReg.Dispose();
        _timeoutCts.Dispose();
    }
}
