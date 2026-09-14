using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Peek.Ipc.Connection;
using Peek.Ipc.Worker;
using Xunit;

namespace Peek.Integration.Tests;

/// <summary>
/// Verifies that a worker dying does not take the whole app down with it, permanently and
/// silently.
/// </summary>
/// <remarks>
/// If recovery guards its own re-entrancy on the published connection state ("return if
/// already Reconnecting") instead of a dedicated flag, one throw inside the recovery path - a
/// transport disposed mid-write - leaves the state stuck on Reconnecting, and since the guard
/// reads that same state, every later trigger (worker exit, transport fault, watchdog) returns
/// immediately. Nothing retries for the rest of the session: the hover highlight freezes, the
/// App and Process pages sit silently empty, and the window stays responsive so nothing looks
/// crashed - the log has it in one repeated line, <c>Worker is not ready (state=Reconnecting)</c>.
/// <para>
/// Every test here kills a real worker process and asserts the connection comes back on its
/// own, because that is the only shape of test that catches this: the build is clean, unit
/// tests pass, and a manual smoke test of a *freshly started* app looks perfect - the failure
/// mode only shows up on the second life of the connection.
/// </para>
/// </remarks>
public sealed class WorkerRecoveryTests
{
    private static WorkerConnectionOptions Options() => new()
    {
        PipeName = $"peek-worker-test-{Guid.NewGuid():N}",
        WorkerExecutablePath = TestPaths.FindWorkerExecutable(),
        ManageWorkerProcess = true,
        WorkerStartupDelay = TimeSpan.FromMilliseconds(300),
        ConnectTimeout = TimeSpan.FromSeconds(10),
        RpcTimeout = TimeSpan.FromSeconds(30),
        // Keeps the recovery assertions below to a few seconds rather than tens.
        WatchdogInterval = TimeSpan.FromSeconds(1),
        ReconnectDelay = TimeSpan.FromMilliseconds(200),
    };

    [Fact]
    public async Task The_connection_recovers_after_the_worker_process_is_killed()
    {
        await using var connection = new WorkerConnection(Options(), NullLoggerFactory.Instance);
        await connection.StartAsync();
        await WaitForReadyAsync(connection);

        var firstPid = connection.WorkerProcessId;
        Assert.NotNull(firstPid);

        KillProcess(firstPid.Value);

        await WaitForReadyAsync(connection, TimeSpan.FromSeconds(45), previousPid: firstPid);

        Assert.NotEqual(firstPid, connection.WorkerProcessId);
    }

    [Fact]
    public async Task Rpc_calls_work_again_after_a_recovery()
    {
        // Getting back to Ready isn't the claim that matters - the claim is that the features
        // built on the connection work again. The App and Process pages are exactly this call.
        await using var connection = new WorkerConnection(Options(), NullLoggerFactory.Instance);
        await connection.StartAsync();
        await WaitForReadyAsync(connection);

        Assert.NotEmpty(await connection.Client.SystemMonitor.EnumerateProcessesAsync(false));

        var previousPid = connection.WorkerProcessId!.Value;
        KillProcess(previousPid);
        await WaitForReadyAsync(connection, TimeSpan.FromSeconds(45), previousPid: previousPid);

        Assert.NotEmpty(await connection.Client.SystemMonitor.EnumerateProcessesAsync(false));
    }

    [Fact]
    public async Task The_connection_recovers_from_being_killed_repeatedly()
    {
        // One recovery working proves the first attempt runs; it does not prove the re-entrancy
        // guard is released afterwards - a re-entrancy guard that never releases would let
        // exactly one recovery cycle start and never a second, so a single-kill test alone
        // would not catch that.
        await using var connection = new WorkerConnection(Options(), NullLoggerFactory.Instance);
        await connection.StartAsync();
        await WaitForReadyAsync(connection);

        for (var round = 1; round <= 3; round++)
        {
            var pid = connection.WorkerProcessId;
            Assert.True(pid.HasValue, $"Round {round}: no worker process is running");

            KillProcess(pid.Value);

            await WaitForReadyAsync(connection, TimeSpan.FromSeconds(45),
                $"Round {round}: the connection never recovered with a new worker", previousPid: pid);
        }
    }

    [Fact]
    public async Task A_started_connection_never_gets_stuck_in_Reconnecting()
    {
        // The state the user's app was wedged in. Reconnecting is legitimate while a cycle is
        // running; what must never happen is it being the *resting* state, because nothing
        // retries from there.
        await using var connection = new WorkerConnection(Options(), NullLoggerFactory.Instance);
        await connection.StartAsync();
        await WaitForReadyAsync(connection);

        var previousPid = connection.WorkerProcessId!.Value;
        KillProcess(previousPid);

        await WaitForReadyAsync(connection, TimeSpan.FromSeconds(45), previousPid: previousPid);
        Assert.Equal(ConnectionState.Ready, connection.CurrentState);
    }

    private static void KillProcess(int pid)
    {
        using var process = Process.GetProcessById(pid);
        process.Kill(entireProcessTree: true);
        process.WaitForExit(TimeSpan.FromSeconds(10));
    }

    private static async Task WaitForReadyAsync(
        WorkerConnection connection,
        TimeSpan? timeout = null,
        string? because = null,
        int? previousPid = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));

        // Ready can still describe the old connection before its exit event is handled.
        while (connection.CurrentState != ConnectionState.Ready ||
               connection.WorkerProcessId is not int currentPid || currentPid == previousPid)
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail(
                    because ?? $"Worker did not become ready (state={connection.CurrentState}, " +
                    $"pid={connection.WorkerProcessId}, previousPid={previousPid})");
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.True(await connection.Client.Automation.PingAsync(TestContext.Current.CancellationToken));
    }
}
