using Peek.Core.Abstractions;

namespace Peek.Core.Services;

public class DisposeService : IDisposeService, IDisposable
{
    private readonly object _lock = new();
    private readonly List<(Action Action, string? Group)> _entries = new();
    private bool _disposed;

    public void Register(IDisposable disposable, string? group = null)
    {
        ArgumentNullException.ThrowIfNull(disposable);
        lock (_lock)
        {
            _entries.Add((() => disposable.Dispose(), group));
        }
    }

    public void Register(Action cleanupAction, string? group = null)
    {
        ArgumentNullException.ThrowIfNull(cleanupAction);
        lock (_lock)
        {
            _entries.Add((cleanupAction, group));
        }
    }

    public void DisposeGroup(string group)
    {
        List<(Action Action, string? Group)> targets;
        lock (_lock)
        {
            targets = _entries.Where(e => e.Group == group).ToList();
            _entries.RemoveAll(e => e.Group == group);
        }
        ExecuteCleanup(targets.Select(e => e.Action));
    }

    public void DisposeAll()
    {
        List<Action> actions;
        lock (_lock)
        {
            // Dispose in reverse registration order (LIFO)
            actions = _entries.Select(e => e.Action).Reverse().ToList();
            _entries.Clear();
        }
        ExecuteCleanup(actions);
    }

    private static void ExecuteCleanup(IEnumerable<Action> actions)
    {
        var exceptions = new List<Exception>();
        foreach (var action in actions)
        {
            try { action(); }
            catch (Exception ex) { exceptions.Add(ex); }
        }

        // Report all failures instead of silently swallowing them
        if (exceptions.Count == 1) throw exceptions[0];
        if (exceptions.Count > 1) throw new AggregateException("One or more cleanup actions failed.", exceptions);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeAll();
    }
}
