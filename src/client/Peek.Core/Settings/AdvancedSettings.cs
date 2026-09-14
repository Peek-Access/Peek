using Microsoft.Extensions.Logging;

namespace Peek.Core.Settings;

public sealed class AdvancedSettings
{
    /// <summary>Overrides WorkerConnectionOptions.WorkerExecutablePath when set - same effect as the Peek_WORKER_PATH env var, exposed as a setting instead.</summary>
    public string? WorkerExecutableOverride { get; set; }

    public LogLevel MinimumLogLevel { get; set; } = LogLevel.Information;
}
