using System.Windows.Automation;
using PeekContracts = Peek.Worker.Contracts.Automation.SemanticElement;
using PeekRect = Peek.Worker.Contracts.Automation.SemanticRect;
using PeekToggleState = Peek.Worker.Contracts.Automation.ToggleState;
using PeekExpandCollapseState = Peek.Worker.Contracts.Automation.ExpandCollapseState;

namespace Peek.Worker.Automation.Windows;

internal static class SemanticElementFactory
{
    public static PeekContracts Create(AutomationElement element, nint fallbackHwnd, string? elementId = null)
    {
        var current = element.Current;

        string? value = null;
        if (TryGetPattern<ValuePattern>(element, ValuePattern.Pattern, out var valuePattern))
        {
            value = SafeGet(() => valuePattern!.Current.Value);
        }

        bool? isSelected = null;
        if (TryGetPattern<SelectionItemPattern>(element, SelectionItemPattern.Pattern, out var selectionPattern))
        {
            isSelected = SafeGet<bool?>(() => selectionPattern!.Current.IsSelected);
        }

        PeekToggleState? toggleState = null;
        if (TryGetPattern<TogglePattern>(element, TogglePattern.Pattern, out var togglePattern))
        {
            toggleState = SafeGet<PeekToggleState?>(() => MapToggleState(togglePattern!.Current.ToggleState));
        }

        PeekExpandCollapseState? expandState = null;
        if (TryGetPattern<ExpandCollapsePattern>(element, ExpandCollapsePattern.Pattern, out var expandPattern))
        {
            expandState = SafeGet<PeekExpandCollapseState?>(() => MapExpandState(expandPattern!.Current.ExpandCollapseState));
        }

        var hwnd = current.NativeWindowHandle != 0
            ? new nint(current.NativeWindowHandle)
            : fallbackHwnd;

        return new PeekContracts
        {
            Name = current.Name ?? string.Empty,
            ControlType = FormatControlType(current.ControlType),
            LocalizedControlType = current.LocalizedControlType ?? string.Empty,
            AutomationId = current.AutomationId ?? string.Empty,
            ClassName = current.ClassName ?? string.Empty,
            ProcessId = current.ProcessId > 0 ? (uint)current.ProcessId : 0,
            Framework = current.FrameworkId ?? string.Empty,
            Rect = ToSemanticRect(current.BoundingRectangle),
            Hwnd = hwnd,
            IsEnabled = current.IsEnabled,
            IsKeyboardFocusable = current.IsKeyboardFocusable,
            IsFocused = current.HasKeyboardFocus,
            IsOffscreen = current.IsOffscreen,
            Value = value,
            IsSelected = isSelected,
            ToggleState = toggleState,
            ExpandState = expandState,
            AcceleratorKey = string.IsNullOrEmpty(current.AcceleratorKey) ? null : current.AcceleratorKey,
            AccessKey = string.IsNullOrEmpty(current.AccessKey) ? null : current.AccessKey,
            HelpText = string.IsNullOrEmpty(current.HelpText) ? null : current.HelpText,
            ItemStatus = string.IsNullOrEmpty(current.ItemStatus) ? null : current.ItemStatus,
            AvailableActions = CollectAvailableActions(element),
            ElementId = elementId,
        };
    }

    internal static bool TryGetPattern<TPattern>(AutomationElement element, AutomationPattern pattern, out TPattern? found)
        where TPattern : class
    {
        try
        {
            if (element.TryGetCurrentPattern(pattern, out var raw) && raw is TPattern typed)
            {
                found = typed;
                return true;
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or System.Runtime.InteropServices.COMException)
        {
        }

        found = null;
        return false;
    }

    internal static T? SafeGet<T>(Func<T> getter)
    {
        try
        {
            return getter();
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or System.Runtime.InteropServices.COMException)
        {
            return default;
        }
    }

    private static string FormatControlType(ControlType? controlType)
    {
        var programmaticName = controlType?.ProgrammaticName;
        if (string.IsNullOrEmpty(programmaticName)) return "Unknown";

        const string prefix = "ControlType.";
        return programmaticName.StartsWith(prefix, StringComparison.Ordinal)
            ? programmaticName[prefix.Length..]
            : programmaticName;
    }

    private static PeekRect ToSemanticRect(System.Windows.Rect rect)
    {
        if (rect.IsEmpty) return new PeekRect();

        return new PeekRect
        {
            Left = (int)rect.Left,
            Top = (int)rect.Top,
            Width = (int)rect.Width,
            Height = (int)rect.Height,
        };
    }

    private static PeekToggleState MapToggleState(ToggleState state) => state switch
    {
        ToggleState.On => PeekToggleState.On,
        ToggleState.Off => PeekToggleState.Off,
        ToggleState.Indeterminate => PeekToggleState.Indeterminate,
        _ => PeekToggleState.Indeterminate,
    };

    private static PeekExpandCollapseState MapExpandState(ExpandCollapseState state) => state switch
    {
        ExpandCollapseState.Collapsed => PeekExpandCollapseState.Collapsed,
        ExpandCollapseState.Expanded => PeekExpandCollapseState.Expanded,
        ExpandCollapseState.PartiallyExpanded => PeekExpandCollapseState.PartiallyExpanded,
        ExpandCollapseState.LeafNode => PeekExpandCollapseState.LeafNode,
        _ => PeekExpandCollapseState.LeafNode,
    };

    // AutomationElementInformation (element.Current) has no IsXPatternAvailable
    // shortcuts - pattern availability is its own property, queried by ID.
    private static List<string> CollectAvailableActions(AutomationElement element)
    {
        var actions = new List<string>(8);
        if (IsPatternAvailable(element, AutomationElement.IsInvokePatternAvailableProperty)) actions.Add("Invoke");
        if (IsPatternAvailable(element, AutomationElement.IsTogglePatternAvailableProperty)) actions.Add("Toggle");
        if (IsPatternAvailable(element, AutomationElement.IsExpandCollapsePatternAvailableProperty)) actions.Add("ExpandCollapse");
        if (IsPatternAvailable(element, AutomationElement.IsSelectionItemPatternAvailableProperty)) actions.Add("Select");
        if (IsPatternAvailable(element, AutomationElement.IsValuePatternAvailableProperty)) actions.Add("SetValue");
        if (IsPatternAvailable(element, AutomationElement.IsRangeValuePatternAvailableProperty)) actions.Add("SetRangeValue");
        if (IsPatternAvailable(element, AutomationElement.IsScrollPatternAvailableProperty)) actions.Add("Scroll");
        if (IsPatternAvailable(element, AutomationElement.IsScrollItemPatternAvailableProperty)) actions.Add("ScrollIntoView");
        if (IsPatternAvailable(element, AutomationElement.IsWindowPatternAvailableProperty)) actions.Add("Window");
        if (IsPatternAvailable(element, AutomationElement.IsTextPatternAvailableProperty)) actions.Add("Text");
        return actions;
    }

    private static bool IsPatternAvailable(AutomationElement element, AutomationProperty property)
    {
        try
        {
            return element.GetCurrentPropertyValue(property) is bool available && available;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }
}
