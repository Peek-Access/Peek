using Peek.Worker.Contracts.Automation;

namespace Peek.Worker.Automation.Windows;

// Every access happens on WindowsAutomationService's single STA dispatcher thread,
// so plain fields/Dictionary are safe here without extra locking.
internal sealed class AutomationElementCache
{
    /// <summary>
    /// Plenty for the handful of windows in play at once (entries expire in ~150ms anyway);
    /// exists only so a long-running session can't accumulate entries indefinitely.
    /// </summary>
    private const int MaxHandleEntries = 128;

    private readonly TimeSpan _ttl;
    private (int X, int Y, SemanticElement Element, DateTime ExpiresAt)? _lastPoint;
    private readonly Dictionary<nint, (SemanticElement Element, DateTime ExpiresAt)> _byHandle = [];

    private long _hits;
    private long _misses;

    public AutomationElementCache(TimeSpan? ttl = null) =>
        _ttl = ttl ?? TimeSpan.FromMilliseconds(150);

    public long Hits => _hits;

    public long Misses => _misses;

    public SemanticElement? TryGetByPoint(int x, int y)
    {
        if (_lastPoint is { } cached && cached.X == x && cached.Y == y && cached.ExpiresAt > DateTime.UtcNow)
        {
            _hits++;
            return cached.Element;
        }

        _misses++;
        return null;
    }

    public void PutByPoint(int x, int y, SemanticElement element) =>
        _lastPoint = (x, y, element, DateTime.UtcNow + _ttl);

    public SemanticElement? TryGetByHandle(nint hwnd)
    {
        if (_byHandle.TryGetValue(hwnd, out var cached))
        {
            if (cached.ExpiresAt > DateTime.UtcNow)
            {
                _hits++;
                return cached.Element;
            }

            // Drop it rather than leaving a dead entry behind: an expired entry that just
            // makes the lookup miss, without being removed, would let this dictionary grow by
            // one permanent entry per distinct window handle the user ever hovered or
            // inspected - each pinning a SemanticElement and its strings for the life of
            // the worker process.
            _byHandle.Remove(hwnd);
        }

        _misses++;
        return null;
    }

    public void PutByHandle(nint hwnd, SemanticElement element)
    {
        if (_byHandle.Count >= MaxHandleEntries)
            EvictExpiredOrOldest();

        _byHandle[hwnd] = (element, DateTime.UtcNow + _ttl);
    }

    /// <summary>
    /// Keeps the handle cache bounded without needing a full LRU: entries live ~150ms, so
    /// sweeping the expired ones reclaims essentially everything in the normal case. Only
    /// if a burst genuinely filled the cache with live entries does it fall back to
    /// dropping the soonest-to-expire one, which is the closest thing to "oldest" available
    /// here and costs at most one re-query.
    /// </summary>
    private void EvictExpiredOrOldest()
    {
        var now = DateTime.UtcNow;
        var expired = _byHandle.Where(kvp => kvp.Value.ExpiresAt <= now).Select(kvp => kvp.Key).ToList();

        foreach (var key in expired)
            _byHandle.Remove(key);

        if (_byHandle.Count < MaxHandleEntries)
            return;

        var soonest = _byHandle.OrderBy(kvp => kvp.Value.ExpiresAt).First().Key;
        _byHandle.Remove(soonest);
    }

    public void Clear()
    {
        _lastPoint = null;
        _byHandle.Clear();
    }
}
