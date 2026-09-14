using Microsoft.Extensions.Logging.Abstractions;
using Peek.Ipc.Connection;
using Peek.Ipc.Worker;


using Xunit;

namespace Peek.Core.Tests.Ipc;

/// <summary>
/// Pins the invariant a started connection must uphold: it must keep trying to reach the
/// worker, and must never come to rest in a state that nothing retries from.
/// </summary>
/// <remarks>
/// These run against a pipe name nobody is serving, so every connect attempt fails - no
/// worker executable, no process management, no timing luck involved. That is deliberately
/// the cheap half of the coverage; WorkerRecoveryTests does the same thing against a real
/// worker process that gets killed. This exercises the *failure* path specifically, which a
/// happy-path test suite or a manual smoke test would never touch.
/// </remarks>
public sealed class WorkerConnectionRecoveryTests
{
    private static WorkerConnectionOptions UnreachableWorker() => new()
    {
        // Nothing ever listens here.
        PipeName = $"peek-worker-nonexistent-{Guid.NewGuid():N}",
        // Don't launch anything - this is purely about how the connection behaves when the
        // other end isn't there.
        ManageWorkerProcess = false,
        WorkerStartupDelay = TimeSpan.Zero,
        ConnectTimeout = TimeSpan.FromMilliseconds(150),
        ReconnectDelay = TimeSpan.FromMilliseconds(10),
        ConnectAttemptsPerCycle = 2,
        WatchdogInterval = TimeSpan.FromMilliseconds(200),
    };

    [Fact]
    public async Task Startup_does_not_throw_when_the_worker_cannot_be_reached()
    {
        // Peek is the thing a user would rely on to tell them something went wrong, so a
        // worker that isn't there must not take the UI down on the way past.
        await using var connection = new WorkerConnection(UnreachableWorker(), NullLoggerFactory.Instance);

        await connection.StartAsync();
    }

    [Fact]
    public async Task A_failed_startup_comes_to_rest_in_Faulted_rather_than_Reconnecting()
    {
        // Reconnecting means "a recovery cycle is running". Resting there is the exact wedge
        // that left a user's install dead: every later trigger saw that state and returned.
        await using var connection = new WorkerConnection(UnreachableWorker(), NullLoggerFactory.Instance);

        await connection.StartAsync();

        Assert.Equal(ConnectionState.Faulted, connection.CurrentState);
    }

    [Fact]
    public async Task The_watchdog_keeps_starting_new_recovery_cycles()
    {
        // The claim: recovery is retried indefinitely, not once. Asserted by counting
        // transitions rather than by looking at the resting state, because the broken version
        // also *looked* settled - it just never moved again.
        await using var connection = new WorkerConnection(UnreachableWorker(), NullLoggerFactory.Instance);
        await connection.StartAsync();

        // Sampled rather than subscribed: the Rx Subscribe(Action<T>) overload lives in an
        // extension namespace this test project doesn't reference, and polling is enough here
        // - the watchdog interval is 200ms, so 3 seconds covers a dozen of them.
        var states = new List<ConnectionState>();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            var current = connection.CurrentState;
            if (states.Count == 0 || states[^1] != current) states.Add(current);
            await Task.Delay(20);
        }

        // Counted on Connecting, not Reconnecting: a cycle passes through Reconnecting far too
        // briefly to sample reliably, whereas each Connecting is one whole new attempt to
        // reach the worker. The broken version produced this list exactly once and then stopped.
        var cyclesStarted = states.Count(s => s == ConnectionState.Connecting);
        Assert.True(cyclesStarted >= 2,
            $"Expected the watchdog to start repeated recovery cycles; saw {cyclesStarted}. " +
            $"States observed: {string.Join(" -> ", states)}");
    }

    [Fact]
    public async Task Client_access_reports_the_state_instead_of_returning_a_dead_client()
    {
        await using var connection = new WorkerConnection(UnreachableWorker(), NullLoggerFactory.Instance);
        await connection.StartAsync();

        var ex = Assert.Throws<InvalidOperationException>(() => connection.Client);

        Assert.Contains("not ready", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
