using Peek.Ipc.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Peek.Ipc.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers WorkerConnection/IPeekWorkerClient for talking to Peek.Worker - the
    /// process that hosts UI Automation and TTS.
    /// </summary>
    public static IServiceCollection AddPeekWorker(
        this IServiceCollection services,
        Action<WorkerConnectionOptions>? configure = null)
    {
        services.TryAddSingleton<WorkerConnectionOptions>(_ =>
        {
            var builder = new WorkerConnectionOptionsBuilder();
            configure?.Invoke(builder.Options);

            var envPath = Environment.GetEnvironmentVariable("Peek_WORKER_PATH");
            if (!string.IsNullOrWhiteSpace(envPath))
                builder.Options = builder.Options with
                {
                    WorkerExecutablePath = envPath
                };

            return builder.Options;
        });

        services.TryAddSingleton<WorkerConnection>();

        return services;
    }
}

internal sealed class WorkerConnectionOptionsBuilder
{
    public WorkerConnectionOptions Options { get; set; } = new();
}
