using System.Windows.Automation;

namespace Peek.Worker.Automation.Windows;

/// <summary>
/// Session cache backing the Element Inspector's lazy tree expansion - distinct from
/// AutomationElementCache (a short-TTL hover cache): entries here live until evicted or
/// explicitly reset (see IAutomationService.ResetInspectorAsync), since an Inspector session
/// is manually driven rather than a high-frequency stream.
///
/// Bounded, because every entry holds a live <see cref="AutomationElement"/> - a COM object
/// that keeps the inspected application's UIA provider alive. Expanding a large tree adds one
/// per node, and without a cap nothing removes them until the user happens to reset the
/// session, so browsing a deep tree for a while would quietly accumulate thousands of
/// cross-process COM references. The cap turns that into a fixed ceiling; the cost of evicting
/// an id that's still on screen is one refresh call, which RefreshInspectorElementAsync
/// already handles by returning null.
///
/// Every access happens on WindowsAutomationService's single STA dispatcher thread
/// (same as AutomationElementCache), so plain fields/Dictionary are safe without extra
/// locking.
/// </summary>
internal sealed class InspectorElementCache
{
    /// <summary>
    /// Comfortably more than a user can have expanded and visible at once, while still
    /// bounding how many cross-process COM references the worker holds.
    /// </summary>
    private const int MaxEntries = 4096;

    private readonly Dictionary<string, (AutomationElement Element, nint Hwnd)> _byId = [];

    /// <summary>Insertion order, for evicting the least-recently-added entries once full.</summary>
    private readonly Queue<string> _insertionOrder = new();

    public string Put(AutomationElement element, nint hwnd)
    {
        if (_byId.Count >= MaxEntries)
            EvictOldest();

        var id = Guid.NewGuid().ToString("N");
        _byId[id] = (element, hwnd);
        _insertionOrder.Enqueue(id);
        return id;
    }

    public (AutomationElement Element, nint Hwnd)? TryGet(string id) =>
        _byId.TryGetValue(id, out var entry) ? entry : null;

    public void Clear()
    {
        _byId.Clear();
        _insertionOrder.Clear();
    }

    /// <summary>
    /// Drops the oldest tenth in one pass rather than one entry per insert - evicting
    /// singly at the cap would re-enter this on every subsequent Put for the rest of the
    /// session.
    /// </summary>
    private void EvictOldest()
    {
        var target = Math.Max(1, MaxEntries / 10);

        for (var i = 0; i < target && _insertionOrder.Count > 0; i++)
            _byId.Remove(_insertionOrder.Dequeue());
    }
}
