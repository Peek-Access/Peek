using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Peek.Worker.Diagnostics;

public sealed class ParentProcessOptions
{
    /// <summary>PID of the process that launched this worker (Peek.Desktop), or null if unset/unparsable.</summary>
    public int? ParentProcessId { get; set; }
}

/// <summary>
/// Backstop for orphan cleanup: watches the launching process (Peek.Desktop, via
/// --parent-pid) and shuts this worker down if it disappears without a graceful
/// WorkerConnection.StopAsync (crash, force-kill, debugger stop, …). A normal exit
/// already stops the worker explicitly - this only covers the case where nothing
/// asked it to.
/// </summary>
public sealed class ParentProcessWatchdog(
    IOptions<ParentProcessOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<ParentProcessWatchdog> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var parentPid = options.Value.ParentProcessId;
        if (parentPid is not { } pid)
        {
            logger.LogInformation("No --parent-pid supplied; not watching a launching process for lifecycle management.");
            return;
        }

        Process parent;
        try
        {
            parent = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            logger.LogWarning("Parent process {Pid} is already gone - stopping", pid);
            lifetime.StopApplication();
            return;
        }

        try
        {
            await parent.WaitForExitAsync(stoppingToken).ConfigureAwait(false);

            if (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Parent process {Pid} exited without a graceful shutdown - stopping", pid);
                lifetime.StopApplication();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path - the worker itself was asked to stop first.
        }
        finally
        {
            parent.Dispose();
        }
    }
}
