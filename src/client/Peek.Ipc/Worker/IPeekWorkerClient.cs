namespace Peek.Ipc.Worker;

/// <summary>
/// The single client-side surface ViewModels/services talk to for Peek.Worker - no
/// caller touches the pipe/JSON-RPC/WorkerRpcChannel types directly (§27).
/// </summary>
public interface IPeekWorkerClient
{
    IAutomationClient Automation { get; }

    ITtsClient Tts { get; }

    IOcrClient Ocr { get; }

    IScreenshotClient Screenshot { get; }

    ILlmClient Llm { get; }

    ISystemMonitorClient SystemMonitor { get; }
}

public sealed class PeekWorkerClient(
    IAutomationClient automation, ITtsClient tts, IOcrClient ocr, IScreenshotClient screenshot, ILlmClient llm,
    ISystemMonitorClient systemMonitor)
    : IPeekWorkerClient
{
    public IAutomationClient Automation { get; } = automation;

    public ITtsClient Tts { get; } = tts;

    public IOcrClient Ocr { get; } = ocr;

    public IScreenshotClient Screenshot { get; } = screenshot;

    public ILlmClient Llm { get; } = llm;

    public ISystemMonitorClient SystemMonitor { get; } = systemMonitor;
}
