using Microsoft.Extensions.Logging;
using Peek.Ipc.Connection;
using Peek.Ipc.Worker;
using ReactiveUI.Primitives;
// Required for the Subscribe(Action<T>) overload - without it the call binds to
// IObservable<T>.Subscribe(IObserver<T>) and fails to compile. See FocusAnnouncer.
using ReactiveUI.Primitives.Extensions;
using ReactiveUI.SourceGenerators;

namespace Peek.Core.ViewModels;

/// <summary>
/// Shared "loading" state and guarded-load wrapper for the monitor-style view models
/// (App Monitor, Process Monitor) that load a list from the worker on open/Refresh.
/// Deliberately not generic over the item type: ReactiveUI.SourceGenerators' [Reactive]
/// does not correctly support a generic base class (verified in isolation - it emits a
/// malformed partial class declaration missing the type parameter list, producing
/// "identifier expected" and leaving IsLoading unresolved). This only factors out the
/// truly-identical IsLoading/try-catch-finally shape; each view model still owns its own
/// collection, fetch call, and ordering.
/// </summary>
public partial class MonitorViewModelBase : ViewModelBase, IDisposable
{
    [Reactive]
    private bool _isLoading;

    private IDisposable? _workerReadySubscription;

    /// <summary>
    /// The UI thread's context, captured at construction. View models are built on the UI
    /// thread, so this is it; Peek.Core deliberately has no Avalonia reference, so this rather
    /// than Dispatcher.UIThread is how work gets back there.
    /// </summary>
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    protected async Task RunLoadAsync(Func<Task> loadAction, ILogger logger, string failureLogMessage)
    {
        IsLoading = true;
        try
        {
            await loadAction();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, failureLogMessage);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Loads as soon as the worker is usable, and again every time it becomes usable after
    /// having gone away. Replaces a bare <c>_ = LoadAsync()</c> in the constructor, which had
    /// two failure modes that both presented identically to the user - a page that is simply
    /// empty, with no error and no retry:
    /// <list type="bullet">
    /// <item>at startup, the view model can be constructed before the worker finishes
    /// connecting, so the one and only load throws "Worker is not ready" and nothing ever
    /// tries again;</item>
    /// <item>when the worker dies and is restarted, the page keeps showing whatever it had
    /// (or nothing) until the user happens to press Refresh.</item>
    /// </list>
    /// <see cref="WorkerConnection.State"/> is a behaviour signal, so an already-Ready
    /// connection loads immediately on subscribe rather than waiting for the next transition.
    /// </summary>
    protected void LoadWhenWorkerReady(
        WorkerConnection connection,
        Func<Task> loadAction,
        ILogger logger,
        string failureLogMessage)
    {
        _workerReadySubscription = connection.State
            .Where(state => state == ConnectionState.Ready)
            .Subscribe(
                onNext: state =>
                {
                    logger.LogDebug("Worker is {State} - loading {ViewModel}", state, GetType().Name);

                    // Marshalled, because these loads refill an ObservableCollection bound to a
                    // DataGrid and only the UI thread may touch one. The first notification is
                    // replayed synchronously on the subscribing (UI) thread and so looked fine;
                    // every later one - i.e. every reload after the worker came back - arrives
                    // on a pool thread and threw "The calling thread cannot access this object
                    // because a different thread owns it", leaving the page empty in exactly
                    // the case this method exists to fix.
                    if (_uiContext is null)
                        _ = RunLoadAsync(loadAction, logger, failureLogMessage);
                    else
                        _uiContext.Post(_ => _ = RunLoadAsync(loadAction, logger, failureLogMessage), null);
                },
                onError: ex => logger.LogError(ex, "Worker-ready subscription faulted"));
    }

    public virtual void Dispose()
    {
        _workerReadySubscription?.Dispose();
        _workerReadySubscription = null;
        GC.SuppressFinalize(this);
    }
}
