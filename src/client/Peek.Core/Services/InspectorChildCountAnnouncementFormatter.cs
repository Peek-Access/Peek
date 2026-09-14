using System.Globalization;
using Peek.Core.Services.Speech;

namespace Peek.Core.Services;

/// <summary>
/// Formats the spoken sentence for how many children a just-expanded Element Inspector
/// node turned out to have - see ElementInspectorViewModel.ExpandNodeAsync. Kept separate
/// from InspectorSelectionAnnouncementFormatter (which announces what a node IS) since
/// this fires on a different action (expanding, not selecting) and says something
/// different (what's inside it).
/// </summary>
public static class InspectorChildCountAnnouncementFormatter
{
    public static string Format(int childCount, CultureInfo culture) => childCount switch
    {
        0 => SpeechStrings.Get("Speech_Inspector_NoChildren", culture),
        1 => SpeechStrings.Get("Speech_Inspector_OneChild", culture),
        _ => SpeechStrings.Format("Speech_Inspector_ChildrenFormat", culture, childCount),
    };
}
