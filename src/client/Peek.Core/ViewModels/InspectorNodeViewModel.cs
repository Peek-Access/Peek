using Peek.Worker.Contracts.Automation;
using Peek.Core.Models;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using System.Collections.ObjectModel;

namespace Peek.Core.ViewModels;

/// <summary>
/// Row for the Element Inspector's hierarchical grid. Wraps either a top-level
/// <see cref="WindowNode"/> (root rows - cheap Win32 enumeration, see WindowEnumerator)
/// or a worker-sourced <see cref="SemanticElement"/> (any UIA automation node) - never
/// both. Children are populated lazily, on first expand, via the worker's
/// automation.inspector.* RPC calls (see ElementInspectorViewModel.LoadChildrenAsync) -
/// a UIA tree can be huge, so nothing here is fetched eagerly.
/// </summary>
public sealed partial class InspectorNodeViewModel : ReactiveObject
{
    public WindowNode? Window { get; }
    public SemanticElement? Element { get; }

    /// <summary>Worker Inspector-session token for this node's own automation element - null for an unexpanded window root (it has no automation element fetched yet).</summary>
    public string? ElementId { get; }

    /// <summary>The window this node lives under - lets a window-root node resolve its own automation element on first expand.</summary>
    public nint Hwnd { get; }

    /// <summary>Hex-formatted for the Element Inspector's compact-width "Handle" column
    /// (see ElementInspectorView's code-behind) - matches the "0x{Hwnd:X}" convention
    /// already used in native-interop log messages elsewhere in this codebase.</summary>
    public string HandleDisplay => $"0x{Hwnd:X}";

    public string Name { get; }
    public string ControlType { get; }
    public string ClassName { get; }
    public string AutomationId { get; }
    public string ProcessName { get; }

    public ObservableCollection<InspectorNodeViewModel> Children { get; } = [];

    [Reactive]
    private bool _isExpanded;
    [Reactive]
    private bool _isLoading;
    [Reactive]
    private bool _loadFailed;

    /// <summary>
    /// Reentrancy guard for ElementInspectorViewModel.ExpandNodeAsync - not bound in XAML,
    /// just plain state. Setting IsExpanded = true can synchronously re-enter
    /// ExpandNodeAsync a second time (the DataGrid's HierarchicalModel reacts to the
    /// property change and re-raises its own NodeExpanded event, which
    /// ElementInspectorView.OnNodeExpanded routes right back into ExpandNodeAsync) -
    /// without this, the keyboard shortcut path double-announces the child count.
    /// </summary>
    public bool IsExpanding { get; set; }

    /// <summary>
    /// False until a fetch has actually run AND succeeded - IsLeafSelector uses this so
    /// an unexpanded node (Children still empty, nothing fetched yet) still shows an
    /// expand arrow instead of looking like a leaf. Deliberately also stays false after
    /// a failed fetch (see MarkLoadFailed) rather than being marked "confirmed empty" -
    /// a window that closed mid-browse or a transient RPC failure should look
    /// retry-able, not indistinguishable from a real leaf element.
    /// </summary>
    public bool ChildrenLoaded { get; private set; }

    public InspectorNodeViewModel(WindowNode window)
    {
        Window = window;
        Hwnd = window.Hwnd;
        Name = string.IsNullOrWhiteSpace(window.Title) ? "(untitled window)" : window.Title;
        ControlType = "Window";
        ClassName = window.ClassName;
        AutomationId = string.Empty;
        ProcessName = window.ProcessName;
    }

    public InspectorNodeViewModel(SemanticElement element, nint hwnd, string processName)
    {
        Element = element;
        ElementId = element.ElementId;
        Hwnd = hwnd;
        Name = string.IsNullOrWhiteSpace(element.Name) ? "(unnamed)" : element.Name;
        ControlType = element.ControlType;
        ClassName = element.ClassName;
        AutomationId = element.AutomationId;
        ProcessName = processName;
    }

    /// <summary>Call on a successful fetch, even if it returned zero children (that's a real, confirmed leaf).</summary>
    public void MarkChildrenLoaded()
    {
        ChildrenLoaded = true;
        LoadFailed = false;
    }

    /// <summary>
    /// Call when a fetch throws - ChildrenLoaded deliberately stays false so re-expanding
    /// (collapse then expand again) retries automatically via ElementInspectorViewModel's
    /// existing "if (ChildrenLoaded || IsLoading) return" guard.
    /// </summary>
    public void MarkLoadFailed() => LoadFailed = true;
}
