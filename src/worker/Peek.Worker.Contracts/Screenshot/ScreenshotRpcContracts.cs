using System.Text.Json.Serialization;
using Peek.Worker.Contracts.Rpc;

namespace Peek.Worker.Contracts.Screenshot;

public sealed class CaptureWindowParams
{
    [JsonPropertyName("hwnd")]
    [JsonConverter(typeof(IntPtrJsonConverter))]
    public required nint Hwnd { get; init; }
}
