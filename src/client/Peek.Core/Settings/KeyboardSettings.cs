namespace Peek.Core.Settings;

/// <summary>
/// Named shortcut bindings. Values are hotkey gesture strings (e.g. "Ctrl+Alt+D").
/// "DescribeFocusedElement"/"ToggleTracking"/"StopSpeaking"/"ShowPeek" are global
/// (system-wide, works-while-unfocused) shortcuts, consumed by
/// <see cref="Services.GlobalHotkeyService"/> via a WH_KEYBOARD_LL hook - "ShowPeek" in
/// particular is what gets a user back to Peek's own window after it's hidden (closing the
/// title bar's X hides rather than quits - see MainWindow.OnClosing) with no tray icon
/// hunting required. Every other entry here is local - a KeyBinding/KeyDown handler the
/// owning view or window itself owns, live the moment it's changed since it only needs that
/// control to have focus, not an OS hook.
/// </summary>
public sealed class KeyboardSettings
{
    public Dictionary<string, string> Shortcuts { get; set; } = new()
    {
        ["DescribeFocusedElement"] = "Ctrl+Alt+D",
        ["ToggleTracking"] = "Ctrl+Alt+T",
        ["StopSpeaking"] = "Ctrl+Alt+S",
        ["ShowPeek"] = "Ctrl+Alt+P",
        ["InspectorToggleExpand"] = "Space",
        ["DockMonitorNext"] = "PageDown",
        ["DockMonitorPrevious"] = "PageUp",
        ["AppMonitorLaunch"] = "Enter",
        ["ProcessMonitorKill"] = "Delete",
        ["ProcessMonitorOpenFolder"] = "Ctrl+E",
    };
}
