using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Peek.Worker.Automation.Windows;
using Peek.Worker.Logging;
using Peek.Worker.Contracts.Automation;
using Peek.Worker.Contracts.Ocr;
using Peek.Worker.Contracts.Screenshot;
using Peek.Worker.Contracts.SystemMonitor;
using Peek.Worker.Contracts.Tts;
using Peek.Worker.Diagnostics;
using Peek.Worker.Llm;
using Peek.Worker.Ocr;
using Peek.Worker.Options;
using Peek.Worker.Rpc;
using Peek.Worker.Screenshot.Windows;
using Peek.Worker.SystemMonitor.Windows;
using Peek.Worker.Tts;
using Peek.Worker.Tts.Windows;

var builder = Host.CreateApplicationBuilder(args);

// Host.CreateApplicationBuilder registers a Console provider by default - drop it (and
// the other Generic Host defaults) in Release. Microsoft.Extensions.Logging.Console's
// SimpleConsole writer isn't safe under the write volume/concurrency a Release install
// runs windowed anyway (see Peek.Worker.csproj) and CreateNoWindow=true
// (WorkerConnection.cs) already hid it even before that: a real crash was reproduced
// from it - System.InvalidOperationException "The stream is currently in use by a
// previous operation on the stream." out of StreamWriter.Flush, caught live in Windows
// Event Viewer during testing.
#if !DEBUG
builder.Logging.ClearProviders();
#endif

// Without this file sink, dropping the console provider above would leave Release
// worker-side diagnostics unrecoverable for an installed app - including exactly the
// kind of startup-crash log used to find the PiperTtsService AOT bug.
builder.Logging.AddProvider(new SimpleFileLoggerProvider("peek-worker"));

// Information by default; PEEK_LOG_LEVEL raises it. Inherited from Peek.Desktop when it
// launches this process, so one variable covers both sides of the pipe - see PeekLogLevel.
builder.Logging.SetMinimumLevel(PeekLogLevel.Resolve());

builder.Services.Configure<WorkerPipeOptions>(options =>
{
    var pipeArgIndex = Array.IndexOf(args, "--pipe-name");
    if (pipeArgIndex >= 0 && pipeArgIndex + 1 < args.Length)
        options.PipeName = args[pipeArgIndex + 1];
});

builder.Services.Configure<PiperTtsOptions>(_ => { });
builder.Services.Configure<PeekOcrOptions>(_ => { });

builder.Services.Configure<ParentProcessOptions>(options =>
{
    var parentPidArgIndex = Array.IndexOf(args, "--parent-pid");
    if (parentPidArgIndex >= 0 && parentPidArgIndex + 1 < args.Length
        && int.TryParse(args[parentPidArgIndex + 1], out var parentPid))
        options.ParentProcessId = parentPid;
});

builder.Services.AddSingleton<IAutomationService, WindowsAutomationService>();
// Speech is registered as Piper-with-a-Windows-fallback rather than Piper alone: Piper
// downloads its runtime and voice model on first use, so a fresh install with no network has
// no speech at all - and in a screen reader the component that breaks is the one that would
// have reported the breakage. SAPI voices ship with Windows and always work, so Peek can
// talk from the first launch and upgrade itself once the neural voice has arrived.
// Both engines are registered concretely so the decorator can hold them directly; only the
// decorator is exposed as ITtsService.
builder.Services.AddSingleton<PiperTtsService>();
builder.Services.AddSingleton<SapiTtsService>();
builder.Services.AddSingleton<ITtsService>(sp =>
{
    // PEEK_TTS_ENGINE=windows forces the fallback engine directly - by a test, or by a user
    // being asked to check whether speech works at all. The SAPI path is, by construction,
    // the one that only runs when something else has already gone wrong, which makes it the
    // one path a normal test, build, or smoke run would never exercise on its own: reaching it
    // requires Piper to fail first, and a trimmed publish that leaves a type it depends on
    // (System.Speech) unrooted would turn its first Speak call into an access violation that
    // takes the whole worker down, invisibly.
    if (string.Equals(Environment.GetEnvironmentVariable("PEEK_TTS_ENGINE"), "windows",
            StringComparison.OrdinalIgnoreCase))
    {
        return sp.GetRequiredService<SapiTtsService>();
    }

    return new FallbackTtsService(
        primary: sp.GetRequiredService<PiperTtsService>(),
        fallback: sp.GetRequiredService<SapiTtsService>(),
        logger: sp.GetRequiredService<ILogger<FallbackTtsService>>());
});
builder.Services.AddSingleton<IOcrService, SimdPaddleOcrService>();
builder.Services.AddSingleton<IScreenshotService, WindowsScreenshotService>();
builder.Services.AddSingleton<ISystemMonitorService, WindowsSystemMonitorService>();
// The worker is stateless with respect to AI settings (§17) - every llm.* RPC call
// carries its own LlmProviderConfig (provider/endpoint/credentials/model) resolved
// client-side from AiSettings, so there is no single startup-registered ILlmProvider
// the way every other worker service has one. See LlmProviderFactory.
builder.Services.AddSingleton<ILlmProviderFactory, LlmProviderFactory>();
builder.Services.AddSingleton<RpcRequestDispatcher>();
builder.Services.AddHostedService<WorkerPipeServer>();
builder.Services.AddHostedService<ParentProcessWatchdog>();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception");

await host.RunAsync();
