using System.Text.Json.Serialization;
using Peek.Worker.Contracts.Rpc;

namespace Peek.Worker.Contracts.Automation;

public sealed class SemanticElement
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("control_type")]
    public string ControlType { get; init; } = "Unknown";

    [JsonPropertyName("localized_control_type")]
    public string LocalizedControlType { get; init; } = string.Empty;

    [JsonPropertyName("automation_id")]
    public string AutomationId { get; init; } = string.Empty;

    [JsonPropertyName("class_name")]
    public string ClassName { get; init; } = string.Empty;

    [JsonPropertyName("process_id")]
    public uint ProcessId { get; init; }

    [JsonPropertyName("framework")]
    public string Framework { get; init; } = string.Empty;

    [JsonPropertyName("rect")]
    public SemanticRect Rect { get; init; } = new();

    [JsonPropertyName("hwnd")]
    [JsonConverter(typeof(IntPtrJsonConverter))]
    public nint Hwnd { get; init; }

    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; init; }

    [JsonPropertyName("is_keyboard_focusable")]
    public bool IsKeyboardFocusable { get; init; }

    [JsonPropertyName("is_focused")]
    public bool IsFocused { get; init; }

    [JsonPropertyName("is_offscreen")]
    public bool IsOffscreen { get; init; }

    [JsonPropertyName("value")]
    public string? Value { get; init; }

    [JsonPropertyName("is_selected")]
    public bool? IsSelected { get; init; }

    [JsonPropertyName("toggle_state")]
    public ToggleState? ToggleState { get; init; }

    [JsonPropertyName("expand_state")]
    public ExpandCollapseState? ExpandState { get; init; }

    [JsonPropertyName("accelerator_key")]
    public string? AcceleratorKey { get; init; }

    [JsonPropertyName("access_key")]
    public string? AccessKey { get; init; }

    [JsonPropertyName("help_text")]
    public string? HelpText { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("item_status")]
    public string? ItemStatus { get; init; }

    [JsonPropertyName("available_actions")]
    public List<string> AvailableActions { get; init; } = [];

    /// <summary>
    /// Opaque token identifying this element in the worker's Inspector session cache
    /// (see automation.inspector.* RPC methods) - only populated by that path, since
    /// caching every hover-path element here would leak memory (hover produces many
    /// elements per second; Inspector sessions are short and explicitly reset).
    /// Null for elements from GetElementFromPointAsync/GetElementFromHandleAsync/GetChildrenAsync.
    /// </summary>
    [JsonPropertyName("element_id")]
    public string? ElementId { get; init; }
}

public sealed class SemanticRect
{
    [JsonPropertyName("left")]
    public int Left { get; init; }

    [JsonPropertyName("top")]
    public int Top { get; init; }

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }
}

public enum ToggleState
{
    Off = 0,
    On = 1,
    Indeterminate = 2,
}

public enum ExpandCollapseState
{
    Collapsed = 0,
    Expanded = 1,
    PartiallyExpanded = 2,
    LeafNode = 3,
}
