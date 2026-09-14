using System.Text.Json.Serialization;
using Peek.Worker.Contracts.Automation;
using Peek.Worker.Contracts.Llm;
using Peek.Worker.Contracts.Ocr;
using Peek.Worker.Contracts.Screenshot;
using Peek.Worker.Contracts.SystemMonitor;
using Peek.Worker.Contracts.Tts;

namespace Peek.Worker.Contracts.Rpc;

[JsonSerializable(typeof(RpcRequest))]
[JsonSerializable(typeof(RpcResponse))]
[JsonSerializable(typeof(RpcError))]
[JsonSerializable(typeof(SemanticElement))]
[JsonSerializable(typeof(SemanticRect))]
[JsonSerializable(typeof(GetElementFromPointParams))]
[JsonSerializable(typeof(GetElementFromHandleParams))]
[JsonSerializable(typeof(GetChildrenParams))]
[JsonSerializable(typeof(ElementResult))]
[JsonSerializable(typeof(ChildrenResult))]
[JsonSerializable(typeof(InspectorGetRootParams))]
[JsonSerializable(typeof(InspectorGetChildrenParams))]
[JsonSerializable(typeof(InspectorRefreshParams))]
[JsonSerializable(typeof(AckResult))]
[JsonSerializable(typeof(WorkerStatus))]
[JsonSerializable(typeof(TtsVoiceInfo))]
[JsonSerializable(typeof(TtsSpeakResult))]
[JsonSerializable(typeof(TtsStatus))]
[JsonSerializable(typeof(SpeakParams))]
[JsonSerializable(typeof(VoicesResult))]
[JsonSerializable(typeof(OcrRegion))]
[JsonSerializable(typeof(OcrQuad))]
[JsonSerializable(typeof(OcrLine))]
[JsonSerializable(typeof(OcrResult))]
[JsonSerializable(typeof(OcrStatus))]
[JsonSerializable(typeof(RecognizeParams))]
[JsonSerializable(typeof(ScreenshotResult))]
[JsonSerializable(typeof(CaptureWindowParams))]
[JsonSerializable(typeof(LlmMessage))]
[JsonSerializable(typeof(LlmProviderConfig))]
[JsonSerializable(typeof(LlmResponse))]
[JsonSerializable(typeof(LlmStreamChunk))]
[JsonSerializable(typeof(LlmStatus))]
[JsonSerializable(typeof(CompleteParams))]
[JsonSerializable(typeof(GetLlmStatusParams))]
[JsonSerializable(typeof(InstalledAppDto))]
[JsonSerializable(typeof(ProcessDto))]
[JsonSerializable(typeof(ActionResult))]
[JsonSerializable(typeof(InstalledAppsResult))]
[JsonSerializable(typeof(LaunchAppParams))]
[JsonSerializable(typeof(EnumerateProcessesParams))]
[JsonSerializable(typeof(ProcessesResult))]
[JsonSerializable(typeof(ProcessActionParams))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public sealed partial class WorkerJsonContext : JsonSerializerContext { }
