using Peek.Worker.Contracts;
using Peek.Worker.Contracts.Automation;

namespace Peek.Ipc.Worker;

/// <summary>
/// Typed async API for querying Peek.Worker's IAutomationService over the
/// peek-worker named pipe. All methods throw <see cref="WorkerRpcException"/>
/// on worker-side errors.
/// </summary>
public interface IAutomationClient
{
    Task<SemanticElement?> GetElementFromPointAsync(int x, int y, CancellationToken ct = default);

    Task<SemanticElement?> GetElementFromHandleAsync(nint hwnd, CancellationToken ct = default);

    /// <summary>The element that currently has keyboard focus anywhere on the desktop - drives follow-the-keyboard announcements.</summary>
    Task<SemanticElement?> GetFocusedElementAsync(CancellationToken ct = default);

    Task<IReadOnlyList<SemanticElement>> GetChildrenAsync(nint hwnd, int depth = 0, CancellationToken ct = default);

    Task ClearCacheAsync(CancellationToken ct = default);

    /// <summary>Element Inspector: gets a window's own automation element as a lazily-expandable tree root.</summary>
    Task<SemanticElement?> GetInspectorRootAsync(nint hwnd, CancellationToken ct = default);

    /// <summary>Element Inspector: direct children (one level) of a previously-returned element, by its ElementId.</summary>
    Task<IReadOnlyList<SemanticElement>> GetInspectorChildrenAsync(string elementId, CancellationToken ct = default);

    /// <summary>Element Inspector: clears the worker's Inspector session cache - call when a session ends.</summary>
    Task ResetInspectorAsync(CancellationToken ct = default);

    /// <summary>Element Inspector: re-reads a previously-returned element's live properties (position, size, state, ...) without re-walking the tree.</summary>
    Task<SemanticElement?> RefreshInspectorElementAsync(string elementId, CancellationToken ct = default);

    Task<WorkerStatus> GetStatusAsync(CancellationToken ct = default);

    Task<bool> PingAsync(CancellationToken ct = default);
}
