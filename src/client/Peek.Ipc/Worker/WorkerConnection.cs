using Microsoft.Extensions.Logging;
using Peek.Ipc.Connection;
using Peek.Ipc.Transport;
using System.ComponentModel;
using System.Diagnostics;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace Peek.Ipc.Worker;

public sealed class WorkerConnection(WorkerConnectionOptions options,
    ILoggerFactory loggerFactory) : IAsyncDisposable
{
    private readonly WorkerConnectionOptions _options = options;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private readonly ILogger<WorkerConnection> _logger = loggerFactory.CreateLogger<WorkerConnection>();

    private readonly BehaviorSignal<ConnectionState> _stateSubject =
        new(ConnectionState.Idle);

    public IObservable<ConnectionState> State => _stateSubject.AsObservable();

    public ConnectionState CurrentState => _stateSubject.Value;

    private NamedPipeTransport? _transport;
    private WorkerRpcChannel? _channel;
    private PeekWorkerClient? _client;
    private IDisposable? _transportStateSub;

    public IPeekWorkerClient Client =>
        _client ?? throw new InvalidOperationException(
            $"Worker is not ready (state={CurrentState})");

    private Process? _workerProcess;

    /// <summary>
    /// PID of the worker process this connection currently manages, or null when none is
    /// running. Exposed so a test can kill the real worker out from under the connection and
    /// assert that it recovers - the failure this guards against (a reconnect that fails once
    /// and is never retried) is invisible to any test that only exercises the happy path.
    /// </summary>
    public int? WorkerProcessId
    {
        get
        {
            var process = _workerProcess;
            try { return process is null || process.HasExited ? null : process.Id; }
            catch (InvalidOperationException) { return null; }
        }
    }

    private CancellationTokenSource _lifetimeCts = new();
    private Task _watchdogLoop = Task.CompletedTask;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    /// <summary>0/1 re-entrancy guard for <see cref="TriggerReconnectAsync"/>.</summary>
    private int _reconnectInFlight;

    public async Task StartAsync(CancellationToken ct = default)
    {
        _lifetimeCts = new CancellationTokenSource();

        try
        {
            SetState(ConnectionState.StartingWorker);
            await LaunchWorkerProcessAsync(ct).ConfigureAwait(false);

            await ConnectWithRetryAsync(_lifetimeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A failed startup must not be terminal. Peek is the thing a user would rely on
            // to tell them something went wrong, so coming up in a state the watchdog can
            // recover from beats throwing out of App startup and taking the window with it.
            _logger.LogError(ex, "Worker startup failed - the watchdog will keep retrying");
        }
        finally
        {
            if (_stateSubject.Value is not (ConnectionState.Ready or ConnectionState.Stopped))
                SetState(ConnectionState.Faulted);
        }

        _watchdogLoop = Task.Run(
            () => WatchdogLoopAsync(_lifetimeCts.Token),
            _lifetimeCts.Token);
    }

    public async Task StopAsync()
    {
        SetState(ConnectionState.Stopped);

        await _lifetimeCts.CancelAsync().ConfigureAwait(false);

        try
        {
            await _watchdogLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        await TearDownStackAsync().ConfigureAwait(false);
        KillWorkerProcess();
    }

    /// <summary>
    /// Resolves <see cref="WorkerConnectionOptions.WorkerExecutablePath"/> to an
    /// absolute path. A rooted value (including one from the Peek_WORKER_PATH env
    /// var - see AddPeekWorker()) is used as-is; otherwise it is resolved against this
    /// process's own executable (AppContext.BaseDirectory). Deliberately not resolved
    /// against Environment.CurrentDirectory, which varies by how the app was launched
    /// (double-click vs. a shortcut vs. a debugger) and is not where the executable
    /// actually lives.
    /// </summary>
    /// <remarks>
    /// Two layouts have to work, because the packaged one and the dev one genuinely differ:
    /// the installed app puts the worker in its own "worker" subfolder (the two exes publish
    /// with different trim settings and must not share a folder - see
    /// <see cref="WorkerConnectionOptions.WorkerExecutablePath"/>), while a plain
    /// <c>dotnet build</c>/F5 loop drops both exes side by side in bin/ (see
    /// Peek.Worker.csproj's OutputPath). The configured path wins; falling back to the bare
    /// filename beside this exe is what keeps F5 working. Without that fallback the dev loop
    /// silently comes up with no worker at all - no automation, no speech - which is a
    /// miserable thing to debug.
    /// </remarks>
    private string ResolveWorkerExecutablePath()
    {
        if (Path.IsPathRooted(_options.WorkerExecutablePath))
            return _options.WorkerExecutablePath;

        var configured = Path.Combine(AppContext.BaseDirectory, _options.WorkerExecutablePath);
        if (File.Exists(configured))
            return configured;

        var sideBySide = Path.Combine(
            AppContext.BaseDirectory,
            Path.GetFileName(_options.WorkerExecutablePath));

        if (File.Exists(sideBySide))
        {
            _logger.LogDebug(
                "Worker not found at {Configured}; using the side-by-side copy at {SideBySide} (dev layout)",
                configured, sideBySide);
            return sideBySide;
        }

        // Neither exists - return the configured path so the caller's not-found error names
        // the location this build is actually expected to ship.
        return configured;
    }

    private async Task LaunchWorkerProcessAsync(CancellationToken ct)
    {
        if (_options.ManageWorkerProcess == false)
        {
            _logger.LogInformation("ManageWorkerProcess=false - assuming Peek.Worker is already running");
            await Task.Delay(_options.WorkerStartupDelay, ct);
            return;
        }

        var exePath = ResolveWorkerExecutablePath();
        _logger.LogInformation("Launching worker: {Path}", exePath);

        if (!File.Exists(exePath))
            throw new FileNotFoundException(
                $"Peek.Worker executable not found. Expected: {exePath} " +
                "(set Peek_WORKER_PATH to override, or pass an absolute WorkerExecutablePath).",
                exePath);

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            // The worker watches this PID and shuts itself down if the launching
            // process disappears without a graceful StopAsync (crash, task-killed, …)
            // so it never outlives the UI as an orphaned process.
            Arguments = $"--pipe-name {_options.PipeName} --parent-pid {Environment.ProcessId}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        _workerProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

        _workerProcess.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                _logger.LogDebug("[worker] {Line}", e.Data);
        };
        _workerProcess.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                _logger.LogDebug("[worker] {Line}", e.Data);
        };
        _workerProcess.Exited += OnWorkerExited;

        _workerProcess.Start();
        _workerProcess.BeginOutputReadLine();
        _workerProcess.BeginErrorReadLine();

        _logger.LogInformation("Worker started (PID={Pid})", _workerProcess.Id);

        await Task.Delay(_options.WorkerStartupDelay, ct);
    }

    private void OnWorkerExited(object? sender, EventArgs e)
    {
        if (_stateSubject.Value == ConnectionState.Stopped) return;

        _logger.LogWarning(
            "Worker process exited unexpectedly (code={Code})",
            _workerProcess?.ExitCode);

        _ = TriggerReconnectAsync();
    }

    /// <summary>
    /// Best-effort recovery - never throws. Called both fire-and-forget (from
    /// OnWorkerExited and the transport-faulted subscription below, neither of which
    /// can observe an exception) and awaited (from the watchdog loop, which has its
    /// own catch but shouldn't have to rely on it). A failure here just means the
    /// connection stays down until the next trigger; it must not crash the process.
    /// </summary>
    /// <remarks>
    /// Re-entrancy is guarded by <see cref="_reconnectInFlight"/> and NOT by the published
    /// connection state ("return if already Reconnecting"): those look equivalent and aren't.
    /// A throw inside this method could leave the state stuck on Reconnecting, and a
    /// state-based guard reading that same state would then return immediately on every later
    /// attempt - worker exit, transport fault, watchdog - forever, with no further log line
    /// than the one failure. Every feature that needs the worker (hover highlight, focus
    /// speech, the App and Process pages) would then fail with
    /// "Worker is not ready (state=Reconnecting)" until the app was restarted.
    /// The <c>finally</c> below is the other half of this guarantee - recovery always lands on
    /// a state the watchdog will pick back up.
    /// </remarks>
    private async Task TriggerReconnectAsync()
    {
        if (_stateSubject.Value == ConnectionState.Stopped) return;

        if (Interlocked.CompareExchange(ref _reconnectInFlight, 1, 0) != 0)
        {
            _logger.LogDebug("Reconnect already in flight - ignoring this trigger");
            return;
        }

        try
        {
            SetState(ConnectionState.Reconnecting);
            await TearDownStackAsync().ConfigureAwait(false);

            if (_options.ManageWorkerProcess)
            {
                KillWorkerProcess();
                try
                {
                    await LaunchWorkerProcessAsync(_lifetimeCts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restart worker process");
                }
            }

            await ConnectWithRetryAsync(_lifetimeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown in progress (StopAsync cancelled _lifetimeCts) - not a failure.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Worker reconnect attempt failed");
        }
        finally
        {
            Interlocked.Exchange(ref _reconnectInFlight, 0);

            // Whatever went wrong, don't leave the connection in a state nothing retries
            // from: Faulted is what WatchdogLoopAsync watches for to start the next attempt.
            if (_stateSubject.Value is not (ConnectionState.Ready or ConnectionState.Stopped))
                SetState(ConnectionState.Faulted);
        }
    }

    private void KillWorkerProcess()
    {
        if (_workerProcess is null) return;
        try
        {
            if (!_workerProcess.HasExited)
            {
                _logger.LogInformation("Killing worker (PID={Pid})", _workerProcess.Id);

                try
                {
                    _workerProcess.Kill(entireProcessTree: false);
                }
                catch (Win32Exception ex) when (_workerProcess.HasExited)
                {
                    _logger.LogDebug(
                        "Worker (PID={Pid}) already exited before Kill completed (Win32={Code})",
                        _workerProcess.Id, ex.NativeErrorCode);
                }
                catch (InvalidOperationException)
                {
                    _logger.LogDebug("Worker process handle was already released");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill worker process");
        }
        finally
        {
            _workerProcess.Exited -= OnWorkerExited;
            _workerProcess.Dispose();
            _workerProcess = null;
        }
    }

    /// <summary>
    /// Bounded on purpose: an unbounded retry loop here means a recovery cycle that can never
    /// connect (worker exe missing, pipe owned by something else) never returns - so the
    /// worker process is never relaunched and the watchdog never gets another turn.
    /// Giving up after <see cref="WorkerConnectionOptions.ConnectAttemptsPerCycle"/> lets the
    /// caller land on Faulted, from which the watchdog starts a *full* cycle: kill, relaunch,
    /// reconnect.
    /// </summary>
    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        await _connectLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (int attempt = 1;
                 attempt <= _options.ConnectAttemptsPerCycle && !ct.IsCancellationRequested;
                 attempt++)
            {
                SetState(ConnectionState.Connecting);
                _logger.LogInformation("Worker connect attempt {Attempt}/{Max}...",
                    attempt, _options.ConnectAttemptsPerCycle);

                try
                {
                    await BuildAndConnectStackAsync(ct).ConfigureAwait(false);
                    SetState(ConnectionState.Ready);
                    _logger.LogInformation("Worker IPC connection established");
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Worker connect attempt {Attempt}/{Max} failed - retrying in {Delay}ms",
                        attempt, _options.ConnectAttemptsPerCycle, _options.ReconnectDelay.TotalMilliseconds);

                    await TearDownStackAsync().ConfigureAwait(false);

                    await Task.Delay(_options.ReconnectDelay, ct).ConfigureAwait(false);
                }
            }

            if (!ct.IsCancellationRequested)
                _logger.LogError(
                    "Could not connect to the worker after {Max} attempts - the watchdog will start a fresh cycle in {Interval}",
                    _options.ConnectAttemptsPerCycle, _options.WatchdogInterval);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task BuildAndConnectStackAsync(CancellationToken ct)
    {
        var transport = new NamedPipeTransport(
            pipeName: _options.PipeName,
            connectTimeout: _options.ConnectTimeout,
            logger: _loggerFactory.CreateLogger<NamedPipeTransport>());

        await transport.ConnectAsync(ct).ConfigureAwait(false);

        var channel = new WorkerRpcChannel(
            transport,
            _loggerFactory.CreateLogger<WorkerRpcChannel>(),
            _options.RpcTimeout);

        var client = new PeekWorkerClient(
            new AutomationClient(channel, _loggerFactory.CreateLogger<AutomationClient>()),
            new TtsClient(channel, _loggerFactory.CreateLogger<TtsClient>()),
            new OcrClient(channel, _loggerFactory.CreateLogger<OcrClient>()),
            new ScreenshotClient(channel, _loggerFactory.CreateLogger<ScreenshotClient>()),
            new LlmClient(channel, _loggerFactory.CreateLogger<LlmClient>()),
            new SystemMonitorClient(channel));

        var sub = transport.State
            .Where(s => s == TransportState.Faulted)
            .Subscribe(state =>
            {
                _logger.LogWarning("Worker transport faulted - scheduling reconnect");
                _ = TriggerReconnectAsync();
            });

        await TearDownStackAsync().ConfigureAwait(false);

        _transport = transport;
        _channel = channel;
        _client = client;
        _transportStateSub = sub;
    }

    /// <summary>
    /// Tears the RPC stack down. Every step is individually guarded because this runs on the
    /// recovery path: the whole point of tearing down is that something is already broken, so
    /// disposing a transport mid-write or a channel with calls in flight can and does throw
    /// (<c>InvalidOperationException: The stream is currently in use by a previous operation
    /// on the stream.</c>). Letting that escape would abort recovery before it had rebuilt
    /// anything, leaving the connection dead for the session.
    /// </summary>
    private async Task TearDownStackAsync()
    {
        try { _transportStateSub?.Dispose(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Disposing the transport-state subscription failed"); }
        _transportStateSub = null;

        try { _channel?.Dispose(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Disposing the RPC channel failed"); }
        _channel = null;
        _client = null;

        var transport = _transport;
        _transport = null;

        if (transport is not null)
        {
            try { await transport.DisconnectAsync().ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogDebug(ex, "Disconnecting the transport failed"); }

            try { transport.Dispose(); }
            catch (Exception ex) { _logger.LogDebug(ex, "Disposing the transport failed"); }
        }
    }

    private async Task WatchdogLoopAsync(CancellationToken ct)
    {
        _logger.LogDebug("Worker watchdog started");

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(_options.WatchdogInterval, ct).ConfigureAwait(false);

            // Idle/Faulted means nothing else is going to bring the connection back: a worker
            // that exited raises OnWorkerExited exactly once, and a recovery that failed has
            // no timer of its own. This loop is that timer - without it, one failed reconnect
            // left the app permanently disconnected.
            if (_stateSubject.Value is ConnectionState.Faulted or ConnectionState.Idle)
            {
                _logger.LogDebug("Watchdog: connection is {State} - starting a recovery cycle",
                    _stateSubject.Value);
                await TriggerReconnectAsync().ConfigureAwait(false);
                continue;
            }

            // StartingWorker/Connecting/Reconnecting: a cycle is already running, leave it be.
            if (_stateSubject.Value != ConnectionState.Ready) continue;

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(_options.WatchdogTimeout);

                var alive = await Client.Automation.PingAsync(timeoutCts.Token).ConfigureAwait(false);

                if (!alive)
                {
                    _logger.LogWarning("Worker watchdog ping returned false");
                    await TriggerReconnectAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Worker watchdog ping failed - triggering reconnect");
                await TriggerReconnectAsync().ConfigureAwait(false);
            }
        }

        _logger.LogDebug("Worker watchdog stopped");
    }

    private void SetState(ConnectionState next)
    {
        var prev = _stateSubject.Value;
        if (prev == next) return;
        _logger.LogInformation("Worker connection: {Prev} -> {Next}", prev, next);
        _stateSubject.OnNext(next);
    }

    public async ValueTask DisposeAsync()
    {
        if (_stateSubject.Value != ConnectionState.Stopped)
            await StopAsync().ConfigureAwait(false);

        _lifetimeCts.Dispose();
        _connectLock.Dispose();
        _stateSubject.OnCompleted();
        _stateSubject.Dispose();
    }
}
