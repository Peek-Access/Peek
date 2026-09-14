using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Peek.Worker.Options;

namespace Peek.Worker.Rpc;

public sealed class WorkerPipeServer : BackgroundService
{
    /// <summary>
    /// How many consecutive failures to stand the listener up before giving up entirely.
    /// At the 200ms retry delay below that's about five seconds.
    /// </summary>
    private const int MaxConsecutiveListenerFailures = 25;

    private readonly RpcRequestDispatcher _dispatcher;
    private readonly WorkerPipeOptions _options;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<WorkerPipeServer> _logger;

    public WorkerPipeServer(
        RpcRequestDispatcher dispatcher,
        IOptions<WorkerPipeOptions> options,
        IHostApplicationLifetime lifetime,
        ILogger<WorkerPipeServer> logger)
    {
        _dispatcher = dispatcher;
        _options = options.Value;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Peek worker listening on pipe '{PipeName}'", _options.PipeName);

        var consecutiveFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    _options.PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                _logger.LogInformation("Client connected");
                consecutiveFailures = 0;

                await HandleClientAsync(pipe, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;

                // Only the first failure in a run is worth a stack trace. This loop used to
                // log an Error with an exception every 200ms forever - a worker that lost
                // the race for its pipe name produced ~5 entries a second, which on one
                // observed machine meant thousands of identical entries and a log growing by
                // roughly a megabyte every five minutes, burying every real diagnostic
                // around it.
                if (consecutiveFailures == 1)
                    _logger.LogError(ex, "Pipe server loop faulted - restarting listener");
                else
                    _logger.LogDebug("Pipe server listener still failing ({Count})", consecutiveFailures);

                if (consecutiveFailures >= MaxConsecutiveListenerFailures)
                {
                    // A worker that cannot own its pipe can never serve anyone, so spinning
                    // is strictly worse than exiting: the client's own launch/retry path can
                    // start a fresh one, and the process stops holding UIA and memory for
                    // nothing.
                    _logger.LogCritical(
                        "Could not listen on pipe '{PipeName}' after {Count} attempts - shutting down. " +
                        "This usually means another process already owns that pipe name.",
                        _options.PipeName, consecutiveFailures);
                    _lifetime.StopApplication();
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200), stoppingToken).ConfigureAwait(false);
            }
            finally
            {
                pipe?.Dispose();
            }
        }

        _logger.LogInformation("Peek worker pipe server stopped");
    }

    /// <summary>
    /// Reads one request line at a time but dispatches each on its own task instead
    /// of awaiting it before reading the next line - a slow request (OCR, TTS, an
    /// LLM completion) must not block the read loop from answering a concurrent
    /// request on the same connection, most importantly the client's own watchdog
    /// ping (WorkerConnection.WatchdogLoopAsync): a serialized loop makes a merely
    /// slow worker indistinguishable from a hung one, which was tearing down and
    /// reconnecting perfectly healthy connections out from under an in-flight call.
    /// Writes are still serialized (one StreamWriter, shared across the in-flight
    /// dispatches) via <paramref name="writeLock"/>; responses can complete out of
    /// order, which is fine - the client matches them back up by request id.
    /// </summary>
    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        var reader = new StreamReader(pipe, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = false,
            NewLine = "\n",
        };
        var writeLock = new SemaphoreSlim(1, 1);
        var inFlight = new ConcurrentDictionary<Task, byte>();

        try
        {
            while (!ct.IsCancellationRequested && pipe.IsConnected)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var requestLine = line;

                // Bound to this connection's own writer/writeLock (below) so a streaming
                // method (llm.completeStream) can write several response lines for one
                // request without racing other concurrent dispatches' final writes - see
                // RpcRequestDispatcher.DispatchAsync/RouteAsync's "llm.completeStream" case.
                async Task EmitAsync(string json, CancellationToken emitCt)
                {
                    await writeLock.WaitAsync(emitCt).ConfigureAwait(false);
                    try
                    {
                        await writer.WriteLineAsync(json.AsMemory(), emitCt).ConfigureAwait(false);
                        await writer.FlushAsync(emitCt).ConfigureAwait(false);
                    }
                    finally
                    {
                        writeLock.Release();
                    }
                }

                var dispatchTask = Task.Run(async () =>
                {
                    string responseJson;
                    try
                    {
                        responseJson = await _dispatcher.DispatchAsync(requestLine, ct, EmitAsync).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // DispatchAsync already turns method/handler failures into a
                        // JSON-RPC error response - reaching here means something
                        // more fundamental broke; just log and drop this response
                        // rather than take the whole connection down over it.
                        _logger.LogError(ex, "Unhandled failure dispatching request");
                        return;
                    }

                    await EmitAsync(responseJson, ct).ConfigureAwait(false);
                }, ct);

                inFlight[dispatchTask] = 0;
                _ = dispatchTask.ContinueWith(
                    t => inFlight.TryRemove(t, out _),
                    TaskContinuationOptions.ExecuteSynchronously);
            }
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Client pipe closed");
        }
        finally
        {
            // Give in-flight dispatches a bounded window to finish (and attempt
            // their write) before tearing down the reader/writer/lock under them;
            // a disconnected client means those writes are moot anyway, so this is
            // a best-effort drain, not a correctness requirement.
            try
            {
                using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                drainCts.CancelAfter(TimeSpan.FromSeconds(5));
                await Task.WhenAll(inFlight.Keys).WaitAsync(drainCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "In-flight requests did not finish draining before disconnect");
            }

            writeLock.Dispose();
            reader.Dispose();
            writer.Dispose();
            _logger.LogInformation("Client disconnected");
        }
    }
}
