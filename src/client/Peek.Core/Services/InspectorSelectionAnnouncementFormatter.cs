using System.Globalization;
using Peek.Core.Services.Speech;

namespace Peek.Core.Services;

/// <summary>
/// Pure text formatting for Element Inspector selection announcements, kept separate
/// from ElementInspectorViewModel's event wiring/IO so the phrasing itself is
/// unit-testable without a real worker/speech service. Phrasing is resolved in the speech
/// culture (see <see cref="SpeechStrings"/>); the element's own name and control type come
/// from the inspected app and are spoken as-is.
/// </summary>
/// <remarks>
/// This is what turns the Element Inspector from a pure debugging tool into something a
/// reading-impaired user can actually use: selecting any node - a window or a UIA
/// element - speaks one coherent sentence naming what it is, what it's called, which
/// process owns it, and exactly where it currently is on screen, while the selection
/// also brings the owning window forward and highlights the element (see
/// ElementInspectorViewModel.OnNodeSelectedAsync).
/// </remarks>
public static class InspectorSelectionAnnouncementFormatter
{
    public static string Format(string name, string controlType, string? processName, int x, int y, int width, int height, CultureInfo culture)
    {
        var namePart = string.IsNullOrWhiteSpace(name)
            ? SpeechStrings.Get("Speech_Inspector_Unnamed", culture)
            : name;
        var processPart = string.IsNullOrWhiteSpace(processName)
            ? string.Empty
            : SpeechStrings.Format("Speech_Inspector_InProcessFormat", culture, processName);

        return SpeechStrings.Format(
            "Speech_Inspector_SelectionFormat", culture,
            namePart, controlType, processPart, x, y, width, height);
    }
}
