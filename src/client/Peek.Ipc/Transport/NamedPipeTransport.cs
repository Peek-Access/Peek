using Microsoft.Extensions.Logging;
using ReactiveUI.Primitives.Signals;
using System.IO.Pipes;
using System.Text;
using ReactiveUI.Primitives;

namespace Peek.Ipc.Transport;

public sealed class NamedPipeTransport : IPipeTransport
{

    private readonly string _pipeName;
    private readonly TimeSpan _connectTimeout;
    private readonly ILogger<NamedPipeTransport> _logger;
    private readonly Signal<string> _lineSubject = new();

    private readonly BehaviorSignal<TransportState> _stateSubject =
        new(TransportState.Disconnected);

    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private CancellationTokenSource? _readCts;
    private Task? _readTask;

    private readonly object _stateLock = new();

    public NamedPipeTransport(
        string pipeName,
        TimeSpan connectTimeout,
        ILogger<NamedPipeTransport> logger)
    {
        _pipeName       = pipeName;
        _connectTimeout = connectTimeout;
        _logger         = logger;
    }
    public IObservable<string> ReceivedLines =>
        _lineSubject.AsObservable();

    public IObservable<TransportState> State =>
        _stateSubject.AsObservable();

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        lock (_stateLock)
        {
            if (_stateSubject.Value == TransportState.Connected) return;
            SetState(TransportState.Connecting);
        }

        _logger.LogInformation("Connecting to pipe: {PipeName}", _pipeName);

        var pipe = new NamedPipeClientStream(
            serverName:        ".",
            pipeName:          _pipeName,
            direction:         PipeDirection.InOut,
            options:           PipeOptions.Asynchronous);

        using var timeoutCts = CancellationTokenSource
            .CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_connectTimeout);

        try
        {
            await pipe.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            SetState(TransportState.Faulted);
            throw new TimeoutException(
                $"Timed out connecting to pipe '{_pipeName}' after {_connectTimeout}");
        }
        catch (Exception ex)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            SetState(TransportState.Faulted);
            _logger.LogError(ex, "Failed to connect to pipe {PipeName}", _pipeName);
            throw;
        }

        lock (_stateLock)
        {
            _pipe   = pipe;
            _writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
            {
                AutoFlush = false,
                NewLine   = "\n"    // LF only – required by the worker's line-based JSON-RPC framing
            };

            _readCts  = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token));

            SetState(TransportState.Connected);
        }

        _logger.LogInformation("Connected to pipe: {PipeName}", _pipeName);
    }

    public async Task SendLineAsync(string line, CancellationToken ct = default)
    {
        if (_stateSubject.Value != TransportState.Connected)
            throw new InvalidOperationException("Transport is not connected.");

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_writer is null) throw new InvalidOperationException("Writer is null.");
            await _writer.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
            await _writer.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Write failed – marking transport as faulted");
            SetState(TransportState.Faulted);
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        CancellationTokenSource? cts;
        Task? readTask;

        lock (_stateLock)
        {
            cts      = _readCts;
            readTask = _readTask;
            _readCts  = null;
            _readTask = null;
        }

        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
            cts.Dispose();
        }

        if (readTask is not null)
        {
            try { await readTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
        }

        // Take the write lock before disposing the writer. Disconnect runs on the recovery
        // path, i.e. exactly when a write is most likely to still be in flight, and disposing
        // a StreamWriter under an in-flight async write throws "The stream is currently in
        // use by a previous operation on the stream." That exception used to escape all the
        // way out of WorkerConnection.TriggerReconnectAsync and abort recovery for good.
        // Bounded wait rather than an unbounded one: a write blocked on a dead pipe must not
        // be able to block teardown either.
        var writeLockTaken = await _writeLock.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        if (!writeLockTaken)
            _logger.LogDebug("A write was still in flight after 2s - disconnecting anyway");

        try
        {
            lock (_stateLock)
            {
                try { _writer?.Dispose(); }
                catch (Exception ex) { _logger.LogDebug(ex, "Disposing the pipe writer failed"); }
                _writer = null;

                try { _pipe?.Dispose(); }
                catch (Exception ex) { _logger.LogDebug(ex, "Disposing the pipe failed"); }
                _pipe = null;

                SetState(TransportState.Disconnected);
            }
        }
        finally
        {
            if (writeLockTaken) _writeLock.Release();
        }

        _logger.LogInformation("Disconnected from pipe: {PipeName}", _pipeName);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        _logger.LogDebug("Read loop started");

        try
        {
            using var reader = new StreamReader(_pipe!, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);

            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);

                if (line is null)
                {
                    _logger.LogInformation("Pipe closed by worker (EOF)");
                    SetState(TransportState.Faulted);
                    break;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    _logger.LogTrace("← {Line}", line.Length > 200
                        ? line[..200] + "…" : line);
                    _lineSubject.OnNext(line);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Read loop faulted");
            SetState(TransportState.Faulted);
        }

        _logger.LogDebug("Read loop stopped");
    }

    private void SetState(TransportState state)
    {
        if (_stateSubject.Value != state)
        {
            _logger.LogDebug("Transport state: {State}", state);
            _stateSubject.OnNext(state);
        }
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
        _lineSubject.OnCompleted();
        _lineSubject.Dispose();
        _stateSubject.OnCompleted();
        _stateSubject.Dispose();
        _writeLock.Dispose();
    }
}
