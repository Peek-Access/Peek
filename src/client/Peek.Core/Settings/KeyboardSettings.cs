namespace Peek.Core.Settings;

/// <summary>
/// Named shortcut bindings. Values are hotkey gesture strings (e.g. "Ctrl+Alt+D").
/// "DescribeFocusedElement"/"ToggleTracking" are global (system-wide,
/// works-while-unfocused) shortcuts, consumed by <see cref="Services.GlobalHotkeyService"/>
/// via a WH_KEYBOARD_LL hook. Every other entry here is local - a KeyBinding/KeyDown handler
/// the owning view or window itself owns, live the moment it's changed since it only needs
/// that control to have focus, not an OS hook.
/// </summary>
public sealed class KeyboardSettings
{
    public Dictionary<string, string> Shortcuts { get; set; } = new()
    {
        ["DescribeFocusedElement"] = "Ctrl+Alt+D",
        ["ToggleTracking"] = "Ctrl+Alt+T",
        ["StopSpeaking"] = "Ctrl+Alt+S",
        ["InspectorToggleExpand"] = "Space",
        ["DockMonitorNext"] = "PageDown",
        ["DockMonitorPrevious"] = "PageUp",
        ["AppMonitorLaunch"] = "Enter",
        ["ProcessMonitorKill"] = "Delete",
        ["ProcessMonitorOpenFolder"] = "Ctrl+E",
    };
}
