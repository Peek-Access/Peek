using System.Globalization;
using System.Text;
using Peek.Worker.Contracts.Automation;

namespace Peek.Core.Services.Speech;

/// <summary>
/// Default <see cref="ISpeechPolicy"/>: "Login, button." / "Remember me, checkbox,
/// checked." rather than just the raw name (§10 examples) - role and state are only
/// spoken when they add information (redundant/empty values are dropped).
/// </summary>
public sealed class StandardSpeechPolicy : ISpeechPolicy
{
    public SpeechContent Describe(SemanticElement element, SpeechVerbosity verbosity = SpeechVerbosity.Standard, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.InvariantCulture;

        var role = HumanizeRole(element);
        var state = DescribeState(element, culture);
        var shortcut = string.IsNullOrEmpty(element.AcceleratorKey) ? element.AccessKey : element.AcceleratorKey;

        return new SpeechContent
        {
            Text = BuildText(element, role, state, shortcut, verbosity, culture),
            Role = role,
            State = state,
            Value = element.Value,
            Shortcut = shortcut,
            Description = element.Description,
        };
    }

    private static string BuildText(SemanticElement element, string? role, string? state, string? shortcut, SpeechVerbosity verbosity, CultureInfo culture)
    {
        var parts = new List<string>(5);
        if (!string.IsNullOrWhiteSpace(element.Name)) parts.Add(element.Name.Trim());

        if (verbosity != SpeechVerbosity.Minimal)
        {
            if (!string.IsNullOrWhiteSpace(role)) parts.Add(role);
            if (!string.IsNullOrWhiteSpace(state)) parts.Add(state);
        }

        if (verbosity is SpeechVerbosity.Detailed or SpeechVerbosity.Verbose)
        {
            // Only speak the value when it adds information beyond the name
            // (e.g. an edit box's typed text), not for controls whose "value" is
            // just a restatement of their name/state.
            if (!string.IsNullOrWhiteSpace(element.Value)
                && !string.Equals(element.Value, element.Name, StringComparison.OrdinalIgnoreCase))
                parts.Add(element.Value.Trim());

            if (!string.IsNullOrWhiteSpace(shortcut))
                parts.Add(SpeechStrings.Format("Speech_ShortcutFormat", culture, shortcut));
        }

        if (verbosity == SpeechVerbosity.Verbose && !string.IsNullOrWhiteSpace(element.Description))
            parts.Add(element.Description.Trim());

        return parts.Count > 0
            ? SpeechStrings.Join(culture, parts)
            : SpeechStrings.Get("Speech_UnlabeledElement", culture);
    }

    private static string? HumanizeRole(SemanticElement element)
    {
        if (!string.IsNullOrWhiteSpace(element.LocalizedControlType))
            return element.LocalizedControlType;

        if (string.IsNullOrWhiteSpace(element.ControlType) || element.ControlType == "Unknown")
            return null;

        // Fallback: PascalCase UIA control type ("CheckBox") -> "check box".
        var sb = new StringBuilder(element.ControlType.Length + 4);
        foreach (var c in element.ControlType)
        {
            if (char.IsUpper(c) && sb.Length > 0) sb.Append(' ');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static string? DescribeState(SemanticElement element, CultureInfo culture)
    {
        string? key = null;

        if (element.ToggleState is { } toggle)
            key = toggle switch
            {
                ToggleState.On => "Speech_State_Checked",
                ToggleState.Off => "Speech_State_Unchecked",
                ToggleState.Indeterminate => "Speech_State_PartiallyChecked",
                _ => null,
            };
        else if (element.IsSelected is true)
            key = "Speech_State_Selected";
        else if (element.ExpandState is { } expand)
            key = expand switch
            {
                ExpandCollapseState.Expanded => "Speech_State_Expanded",
                ExpandCollapseState.Collapsed => "Speech_State_Collapsed",
                _ => null,
            };
        else if (!element.IsEnabled)
            key = "Speech_State_Disabled";

        return key is null ? null : SpeechStrings.Get(key, culture);
    }
}
