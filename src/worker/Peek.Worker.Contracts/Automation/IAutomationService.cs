namespace Peek.Worker.Contracts.Automation;

public interface IAutomationService
{
    Task<SemanticElement?> GetElementFromPointAsync(int x, int y, CancellationToken ct = default);

    Task<SemanticElement?> GetElementFromHandleAsync(nint hwnd, CancellationToken ct = default);

    /// <summary>
    /// The element that currently has keyboard focus, anywhere on the desktop - the
    /// basis for follow-the-keyboard announcements (see FocusTracker on the client),
    /// which is how someone who can't aim a mouse at what they can't see actually
    /// drives a screen reader. Deliberately uncached: focus is the one thing that must
    /// always be read live, and the hover cache is keyed by point/handle anyway.
    /// Null when nothing is focused or the focused element dies mid-read.
    /// </summary>
    Task<SemanticElement?> GetFocusedElementAsync(CancellationToken ct = default);

    Task<IReadOnlyList<SemanticElement>> GetChildrenAsync(nint hwnd, int depth = 0, CancellationToken ct = default);

    Task ClearCacheAsync(CancellationToken ct = default);

    /// <summary>
    /// Element Inspector support (see docs on <see cref="SemanticElement.ElementId"/>):
    /// gets a window's own automation element as the root of a lazily-expandable tree,
    /// registering it in a dedicated session cache distinct from the hover-path cache.
    /// </summary>
    Task<SemanticElement?> GetInspectorRootAsync(nint hwnd, CancellationToken ct = default);

    /// <summary>Direct children (one level, lazy) of a previously-returned Inspector element, looked up by its ElementId.</summary>
    Task<IReadOnlyList<SemanticElement>> GetInspectorChildrenAsync(string elementId, CancellationToken ct = default);

    /// <summary>Clears the Inspector session cache - call when an Inspector session ends (dialog closed) or a new root is picked.</summary>
    Task ResetInspectorAsync(CancellationToken ct = default);

    /// <summary>
    /// Re-reads a previously-returned Inspector element's live properties (position,
    /// size, state, ...) without re-walking the tree - used when the user selects a
    /// node to announce/highlight its current on-screen location, which can have
    /// changed since it was originally fetched. Null if the element is gone (window
    /// closed, cache reset).
    /// </summary>
    Task<SemanticElement?> RefreshInspectorElementAsync(string elementId, CancellationToken ct = default);

    AutomationDiagnostics GetDiagnostics();
}

public readonly record struct AutomationDiagnostics(long CacheHits, long CacheMisses);
