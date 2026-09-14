namespace Peek.Ipc.Worker;

public sealed record WorkerConnectionOptions
{
    /// <summary>
    /// Named pipe to connect to. Passed to the worker via --pipe-name when this connection
    /// launches it (see WorkerConnection.LaunchWorkerProcessAsync), so the two always agree.
    /// </summary>
    /// <remarks>
    /// Unique per client process. A named pipe is machine-wide and WorkerPipeServer serves
    /// exactly one instance of it, so a fixed, shared name would let a single leftover worker -
    /// one orphaned by a crash, a force-kill, or a debugger stop - own that name forever and
    /// starve every worker launched afterwards.
    /// <para>
    /// With a per-process name an orphan can only ever hold a pipe nobody wants, and two
    /// Peek instances (or a test run alongside a real one) can't collide either.
    /// </para>
    /// </remarks>
    public string PipeName { get; init; } = DefaultPipeName;

    private static readonly string DefaultPipeName =
        $"peek-worker-{Environment.ProcessId}-{Guid.NewGuid():N}";

    /// <summary>
    /// Path to the Peek.Worker executable. Relative to <see cref="AppContext.BaseDirectory"/>
    /// when not rooted (see WorkerConnection.ResolveWorkerExecutablePath) - defaults to a
    /// "worker" subfolder next to Peek.Desktop.exe, not the same folder, because the two
    /// exes are published with different trim settings (Peek.Worker.csproj enables
    /// PublishTrimmed, Peek.Desktop.csproj deliberately doesn't - see that file's comment).
    /// Publishing both into one shared folder let one exe's independently-trimmed copy of a
    /// shared runtime file (e.g. System.Private.CoreLib.dll) silently overwrite the other's,
    /// since trim reachability is calculated per-app - reproduced this concretely before
    /// splitting the two into separate folders.
    /// </summary>
    public string WorkerExecutablePath { get; init; } = "worker\\Peek.Worker.exe";

    /// <summary>
    /// When true, WorkerConnection launches and manages the worker process.
    /// Set to false if the worker is already running externally (e.g. during dev).
    /// </summary>
    public bool ManageWorkerProcess { get; init; } = true;

    /// <summary>How long to wait after launching the worker before connecting.</summary>
    public TimeSpan WorkerStartupDelay { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Timeout for the initial pipe connect call.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Default per-RPC call timeout.</summary>
    public TimeSpan RpcTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Delay between reconnect attempts.</summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How many pipe-connect attempts one recovery cycle makes before giving up and letting
    /// the watchdog start a fresh cycle (which kills and relaunches the worker process first).
    /// Retrying the connect forever inside a single cycle looks more persistent but is
    /// strictly worse: the one thing most likely to fix an unreachable worker is restarting
    /// it, and that only happens at the start of a cycle.
    /// </summary>
    public int ConnectAttemptsPerCycle { get; init; } = 5;

    /// <summary>How often the watchdog pings the worker.</summary>
    public TimeSpan WatchdogInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Watchdog ping timeout - triggers reconnect if exceeded.</summary>
    public TimeSpan WatchdogTimeout { get; init; } = TimeSpan.FromMilliseconds(1000);
}
