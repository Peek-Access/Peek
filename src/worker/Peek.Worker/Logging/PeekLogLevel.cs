using Microsoft.Extensions.Logging;

namespace Peek.Worker.Logging;

/// <summary>
/// Resolves the minimum log level for an installed build from the <c>PEEK_LOG_LEVEL</c>
/// environment variable, defaulting to <see cref="LogLevel.Information"/>.
/// </summary>
/// <remarks>
/// Diagnosing a packaged install used to mean building a special binary and shipping it to
/// whoever hit the problem, because the Release level is baked in at compile time. The whole
/// value of the file sink is being able to ask "turn this on and send me the log" instead -
/// so the level has to be changeable without a rebuild.
/// <para>
/// Deliberately an environment variable rather than a setting in settings.json: the failures
/// worth raising the level for include the ones that happen before (or because) settings
/// load, and a user who has been told to run
/// <c>set PEEK_LOG_LEVEL=Debug &amp;&amp; Peek.Desktop.exe</c> gets a debug session without
/// touching any persisted state - the next normal launch is back to Information on its own.
/// </para>
/// <para>
/// The worker inherits this automatically: it is launched by the client (see
/// WorkerConnection.LaunchWorkerProcessAsync) and a child process inherits its parent's
/// environment, so one variable raises the level on both sides of the pipe - which matters
/// because most interesting failures here span both.
/// </para>
/// </remarks>
public static class PeekLogLevel
{
    public const string EnvironmentVariable = "PEEK_LOG_LEVEL";

    public static LogLevel Resolve()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(configured) &&
            Enum.TryParse<LogLevel>(configured, ignoreCase: true, out var level))
        {
            return level;
        }

#if DEBUG
        return LogLevel.Debug;
#else
        return LogLevel.Information;
#endif
    }
}
