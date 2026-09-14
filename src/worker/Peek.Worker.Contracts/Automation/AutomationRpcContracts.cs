using System.Text.Json.Serialization;
using Peek.Worker.Contracts.Rpc;

namespace Peek.Worker.Contracts.Automation;

public sealed class GetElementFromPointParams
{
    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }
}

public sealed class GetElementFromHandleParams
{
    [JsonPropertyName("hwnd")]
    [JsonConverter(typeof(IntPtrJsonConverter))]
    public nint Hwnd { get; init; }
}

public sealed class GetChildrenParams
{
    [JsonPropertyName("hwnd")]
    [JsonConverter(typeof(IntPtrJsonConverter))]
    public nint Hwnd { get; init; }

    [JsonPropertyName("depth")]
    public int? Depth { get; init; }
}

public sealed class ElementResult
{
    [JsonPropertyName("element")]
    public SemanticElement? Element { get; init; }
}

public sealed class ChildrenResult
{
    [JsonPropertyName("items")]
    public List<SemanticElement> Items { get; init; } = [];
}

public sealed class AckResult
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }
}
