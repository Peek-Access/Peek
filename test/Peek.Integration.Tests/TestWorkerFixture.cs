using Microsoft.Extensions.Logging.Abstractions;
using Peek.Ipc.Worker;
using Peek.Ipc.Connection;
using Xunit;

namespace Peek.Integration.Tests;

/// <summary>
/// Launches a real, isolated <c>Peek.Worker.exe</c> for the lifetime of a test
/// class and exposes a connected <see cref="WorkerConnection"/> to it - this is
/// the "UI &lt;-&gt; worker communication" layer: no fakes, no in-process shortcuts,
/// the exact same <see cref="WorkerConnection"/>/JSON-RPC-over-named-pipe path
/// Peek.Desktop itself uses.
/// </summary>
/// <remarks>
/// Uses a per-run unique pipe name (<see cref="WorkerConnectionOptions.PipeName"/>)
/// rather than the default "peek-worker" - <c>WorkerPipeServer</c> accepts exactly
/// one client connection at a time, so sharing the default name with a real running
/// Peek.Desktop (or another concurrent test run) would starve one of them.
/// </remarks>
public sealed class TestWorkerFixture : IAsyncLifetime
{
    public WorkerConnection Connection { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var options = new WorkerConnectionOptions
        {
            PipeName = $"peek-worker-test-{Guid.NewGuid():N}",
            WorkerExecutablePath = TestPaths.FindWorkerExecutable(),
            ManageWorkerProcess = true,
            WorkerStartupDelay = TimeSpan.FromMilliseconds(300),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            // Not the 2-second production default. That value is a *latency budget* for
            // hover - it exists so a slow automation call gets abandoned rather than making
            // the UI feel stuck - and inheriting it here turns "is this correct?" into "is
            // this fast?". On a cold CI runner the very first UIA call pays for initialising
            // the whole UIA COM stack and routinely exceeds 2s, which failed five tests on
            // RPC ids 1-3 while the same tests passed locally.
            RpcTimeout = TimeSpan.FromSeconds(30),
        };

        Connection = new WorkerConnection(options, NullLoggerFactory.Instance);
        await Connection.StartAsync();

        using var readyCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (Connection.CurrentState != ConnectionState.Ready)
        {
            readyCts.Token.ThrowIfCancellationRequested();
            await Task.Delay(50, readyCts.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync();
    }
}
