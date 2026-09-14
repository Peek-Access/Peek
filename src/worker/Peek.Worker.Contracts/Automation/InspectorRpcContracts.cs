using System.Text.Json.Serialization;
using Peek.Worker.Contracts.Rpc;

namespace Peek.Worker.Contracts.Automation;

public sealed class InspectorGetRootParams
{
    [JsonPropertyName("hwnd")]
    [JsonConverter(typeof(IntPtrJsonConverter))]
    public nint Hwnd { get; init; }
}

public sealed class InspectorGetChildrenParams
{
    [JsonPropertyName("element_id")]
    public string ElementId { get; init; } = string.Empty;
}

public sealed class InspectorRefreshParams
{
    [JsonPropertyName("element_id")]
    public string ElementId { get; init; } = string.Empty;
}
