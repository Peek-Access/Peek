using Xunit;

namespace Peek.Integration.Tests;

/// <summary>
/// Exercises the apps.* / processes.* RPC methods (App Monitor / Process Monitor support)
/// against a real worker - the same real-registry/real-process-list path
/// AppMonitorViewModel/ProcessMonitorViewModel rely on, not a fake.
/// </summary>
public sealed class SystemMonitorRoundTripTests : IClassFixture<TestWorkerFixture>
{
    private readonly TestWorkerFixture _fixture;

    public SystemMonitorRoundTripTests(TestWorkerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EnumerateApps_returns_at_least_one_named_entry()
    {
        var apps = await _fixture.Connection.Client.SystemMonitor.EnumerateAppsAsync();

        // Every real Windows machine has at least a handful of Programs-and-Features
        // entries (redistributables, drivers, ...) - an empty list would mean the
        // registry read itself is broken, not that the machine genuinely has zero apps.
        Assert.NotEmpty(apps);
        Assert.All(apps, a => Assert.False(string.IsNullOrWhiteSpace(a.Name)));
    }

    [Fact]
    public async Task LaunchApp_with_an_unknown_key_fails_rather_than_throwing()
    {
        var result = await _fixture.Connection.Client.SystemMonitor.LaunchAppAsync("not-a-real-key");

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task EnumerateProcesses_includes_this_test_processes_own_pid()
    {
        var currentPid = Environment.ProcessId;

        var processes = await _fixture.Connection.Client.SystemMonitor.EnumerateProcessesAsync(includeSystemProcesses: true);

        Assert.Contains(processes, p => p.Id == currentPid);
    }

    [Fact]
    public async Task EnumerateProcesses_excludes_system_processes_by_default_unless_requested()
    {
        var withoutSystem = await _fixture.Connection.Client.SystemMonitor.EnumerateProcessesAsync(includeSystemProcesses: false);
        var withSystem = await _fixture.Connection.Client.SystemMonitor.EnumerateProcessesAsync(includeSystemProcesses: true);

        Assert.DoesNotContain(withoutSystem, p => p.IsSystemProcess);
        // The System process itself (pid 4) or another session-0 process should show up
        // once system processes are requested - if this ever flakes because pid 4 doesn't
        // exist, any IsSystemProcess=true entry proves the include flag actually worked.
        Assert.Contains(withSystem, p => p.IsSystemProcess);
    }

    [Fact]
    public async Task KillProcess_of_an_unknown_pid_fails_rather_than_throwing()
    {
        // A pid essentially guaranteed not to exist.
        var result = await _fixture.Connection.Client.SystemMonitor.KillProcessAsync(int.MaxValue - 1);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task KillProcess_of_a_system_process_is_refused()
    {
        // pid 4 is the Windows "System" process (session 0) on every real Windows machine.
        var result = await _fixture.Connection.Client.SystemMonitor.KillProcessAsync(4);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task OpenProcessFolder_of_an_unknown_pid_fails_rather_than_throwing()
    {
        var result = await _fixture.Connection.Client.SystemMonitor.OpenProcessFolderAsync(int.MaxValue - 1);

        Assert.False(result.Success);
    }
}
