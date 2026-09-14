using System.Collections.Concurrent;

namespace Peek.Worker.Automation.Windows;

// UI Automation client calls are COM calls; routing every request through one
// dedicated STA thread avoids apartment/reentrancy issues that show up
// intermittently when calling AutomationElement from arbitrary thread-pool threads.
internal sealed class StaThreadDispatcher : IDisposable
{
    // How long a single queued call is allowed to occupy the STA thread before it's
    // treated as stuck. A COM call against a genuinely unresponsive target process
    // (a hung app, a modal dialog holding its owner's message loop) can block
    // indefinitely - COM calls aren't preemptible from another thread, so the only way
    // to keep the rest of the queue moving (hover on a different, healthy window,
    // Inspector browsing, screen-analysis snapshots) is to abandon the stuck thread and
    // start a fresh one for whatever is still waiting behind it. The abandoned thread
    // keeps running the one hung call, orphaned, for the life of the process - a
    // deliberate, bounded (one per hang) thread leak traded for not wedging the whole
    // automation pipeline; it's a background thread, so it doesn't block process exit.
    private static readonly TimeSpan StuckThreshold = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(2);

    private readonly string _threadName;
    private readonly object _gate = new();
    private readonly Timer _watchdog;
    private BlockingCollection<Action> _queue;
    private Thread _thread;
    private DateTime _itemStartedUtc;
    private bool _itemRunning;
    private bool _disposed;

    public StaThreadDispatcher(string threadName)
    {
        _threadName = threadName;
        _queue = new BlockingCollection<Action>();
        _thread = StartThread(_queue);
        _watchdog = new Timer(_ => CheckForStuckThread(), null, WatchdogInterval, WatchdogInterval);
    }

    public Task<T> InvokeAsync<T>(Func<T> func, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ctReg = ct.Register(() => tcs.TrySetCanceled(ct));

        var action = () =>
        {
            try
            {
                if (ct.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(ct);
                    return;
                }
                tcs.TrySetResult(func());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            finally
            {
                ctReg.Dispose();
            }
        };

        lock (_gate)
        {
            _queue.Add(action);
        }

        return tcs.Task;
    }

    public Task InvokeAsync(Action action, CancellationToken ct = default) =>
        InvokeAsync(() =>
        {
            action();
            return true;
        }, ct);

    private Thread StartThread(BlockingCollection<Action> queue)
    {
        var thread = new Thread(() => RunLoop(queue))
        {
            IsBackground = true,
            Name = _threadName,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread;
    }

    private void RunLoop(BlockingCollection<Action> queue)
    {
        foreach (var action in queue.GetConsumingEnumerable())
        {
            lock (_gate)
            {
                _itemStartedUtc = DateTime.UtcNow;
                _itemRunning = true;
            }
            try
            {
                action();
            }
            finally
            {
                lock (_gate)
                {
                    _itemRunning = false;
                }
            }
        }
    }

    private void CheckForStuckThread()
    {
        lock (_gate)
        {
            if (_disposed || !_itemRunning) return;
            if (DateTime.UtcNow - _itemStartedUtc < StuckThreshold) return;
            if (_queue.Count == 0) return; // nothing waiting behind it yet - let it keep running

            var stuckQueue = _queue;
            _queue = new BlockingCollection<Action>();
            _thread = StartThread(_queue);

            // Move everything still waiting (not the one action already mid-flight on the
            // abandoned thread - that's no longer in stuckQueue's buffer, only taken items
            // are removed) onto the fresh queue.
            stuckQueue.CompleteAdding();
            foreach (var pending in stuckQueue.GetConsumingEnumerable())
                _queue.Add(pending);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _queue.CompleteAdding();
        }
        _watchdog.Dispose();
        _thread.Join(TimeSpan.FromSeconds(5));
        _queue.Dispose();
    }
}
