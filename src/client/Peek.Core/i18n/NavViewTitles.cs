using System.Globalization;
using Peek.Core.Services.Speech;

namespace Peek.Core.i18n;

/// <summary>
/// Resolves one of the fixed navigable view names (region navigation targets like
/// "SettingsView") to the same localized title shown in MainView's nav sidebar - shared so a
/// spoken page-change announcement never drifts out of sync with the label a sighted user
/// sees for the same page.
/// </summary>
public static class NavViewTitles
{
    private static readonly Dictionary<string, string> KeyByViewName = new(StringComparer.Ordinal)
    {
        ["ScreenReaderView"] = "Nav_ScreenReader",
        ["ElementInspectorView"] = "Nav_ElementInspector",
        ["AppMonitorView"] = "Nav_AppMonitor",
        ["ProcessMonitorView"] = "Nav_ProcessMonitor",
        ["SettingsView"] = "Nav_Settings",
    };

    public static string Resolve(string viewName, CultureInfo culture) =>
        KeyByViewName.TryGetValue(viewName, out var key) ? SpeechStrings.Get(key, culture) : viewName;
}
