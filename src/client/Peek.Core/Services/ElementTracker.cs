//using DynamicData.Binding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Peek.Worker.Contracts.Automation;
using Peek.Core.Abstractions;
using Peek.Core.Services.Ocr;
using Peek.Core.Services.Speech;
using Peek.Core.Settings;
using Peek.Ipc.Worker;
using Peek.Ipc.Connection;
using Peek.Ipc.Extensions;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;
using ReactiveUI.Primitives.Extensions;
using ReactiveUI.Primitives.Signals;
using ReactiveUI.SourceGenerators;
using System.Diagnostics;
using ConnectionState = Peek.Ipc.Connection.ConnectionState;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Peek.Core.Services;

public partial class ElementTracker : ReactiveObject, IDisposable
{
    private readonly MultipleDisposable _disposables = [];
    private readonly SingleReplaceableDisposable _trackingDisposable = new();
    private readonly WorkerConnection _workerConnection;
    private readonly IMouseTracker _mouseTracker;
    private readonly ILogger _logger;
    private readonly IHighlightService _highlightService;
    private readonly IAccessibilitySpeechService _speechService;
    private readonly OcrFallbackAnnouncer _ocrFallback;
    private readonly ISettingsService _settings;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private bool _trackingStarted;
    private bool _disposed;

    /// <summary>
    /// The latest raw mouse position, tracked independently of which UIA element it resolves to
    /// - needed because an OCR-opaque window's element identity never changes as the cursor
    /// moves within it (see the OCR-position-follow subscription below), so the resolved
    /// SemanticElement alone can't tell OcrFallbackAnnouncer what's actually under the cursor.
    /// </summary>
    private System.Drawing.Point _lastMousePosition;

    /// <summary>
    /// Hovered elements, published on whatever background thread resolved them, for the
    /// announcement pipeline to consume. Separate from the <see cref="CurrentElement"/>
    /// property on purpose: that one is UI-bound, and deriving speech from it is what put
    /// speech on the UI thread.
    /// </summary>
    private readonly Signal<SemanticElement> _hoveredElements = new();

    /// <summary>The UI thread's context, captured at construction (this is built on it).</summary>
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread, or inline when there is no UI thread to
    /// go to. The inline path is not a detail: without it, a host with no synchronization
    /// context (the integration tests, and any headless use) silently drops the work entirely -
    /// which showed up as CurrentElement never being set and hovering appearing to do nothing.
    /// </summary>
    private void OnUiThread(Action action)
    {
        if (_uiContext is null) action();
        else _uiContext.Post(_ => action(), null);
    }

    /// <summary>
    /// How often the cursor's latest position is turned into a "what's under it?" query, and
    /// therefore how often the highlight box can move. A <c>Sample</c> window, not a debounce -
    /// it emits continuously while the mouse is moving (see TrackMouseElement).
    /// </summary>
    /// <remarks>
    /// Chosen to sit just above the ~20ms mouse sampling interval so effectively every mouse
    /// sample produces a query, which is what makes the box feel welded to the cursor. Raising
    /// this well past the sampling interval to cut query volume would make the debounce real
    /// and the highlight visibly laggy - the fix for query volume is the right operator
    /// (<c>Sample</c>, not <c>Throttle</c>/debounce), not a larger number here.
    /// <para>
    /// Volume is bounded twice over: by this window, and by <c>Switch</c>, which cancels the
    /// in-flight query as soon as a newer position arrives, so at most one is ever running.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan HighlightFollowInterval = TimeSpan.FromMilliseconds(25);

    [Reactive]
    private SemanticElement? _currentElement;
    [Reactive]
    private ConnectionState _workerState;
    [Reactive]
    private string _statusText = "";
    [Reactive]
    private bool _isTracking;
    public ElementTracker(IServiceProvider serviceProvider, ILogger<ElementTracker> logger)
    {
        _logger = logger;

        _sw.Restart();
        _mouseTracker = serviceProvider.GetRequiredService<IMouseTracker>();
        _workerConnection = serviceProvider.GetRequiredService<WorkerConnection>();
        _highlightService = serviceProvider.GetRequiredService<IHighlightService>();
        _speechService = serviceProvider.GetRequiredService<IAccessibilitySpeechService>();
        _ocrFallback = serviceProvider.GetRequiredService<OcrFallbackAnnouncer>();
        _settings = serviceProvider.GetRequiredService<ISettingsService>();
        _trackingDisposable.DisposeWith(_disposables);
        _ = InitializeAsync().ContinueWith(
            t => _logger.LogCritical(t.Exception, "ElementTracker initialization failed"),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

        this.WhenAnyValue(x => x.IsTracking)
            .Skip(1)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SelectMany(on => on
                ? Signal.FromAsync(StartTrackingCore)
                : Signal.FromAsync(StopTrackingCore))
            .Subscribe()
            .DisposeWith(_disposables);

        // Subscribed here in the constructor, not in InitializeAsync: _hoveredElements is a hot
        // stream with no replay, and InitializeAsync doesn't get this far until after the worker
        // has started and a one-second settle delay has elapsed. Any element hovered in that
        // window would be published to nobody and silently never spoken.
        //
        // Announcements run entirely off the UI thread, on their own stream fed by the hover
        // subscription (see StartTrackingCore). Two operators carry the whole behaviour here,
        // and they are deliberately NOT the one the highlight path uses:
        //
        //   DistinctUntilChanged(ByIdentity) - SemanticElement is a class, so the default
        //     reference comparison treats every query result as a new element. Drifting the
        //     cursor across one big button re-announced it over and over, each announcement
        //     cutting off the last mid-word - which is what "the speech is chaotic when I move
        //     fast" actually was. Compared by what makes a control *that control* instead.
        //
        //   Throttle (= debounce) - speak only once the hovered element has been stable for
        //     the settle window. This is the right operator here for exactly the reason it was
        //     the wrong one for the highlight: while the mouse is moving it emits nothing, so
        //     sweeping across twenty controls announces none of them, and the one the user
        //     stops on gets spoken cleanly and once.
        //
        // Gated live by AnnounceOnHover rather than at subscribe time, so toggling it in
        // Settings takes effect on the next hover with no restart - and turning it off still
        // leaves the highlight and the Element Details panel following the mouse, which is the
        // point: someone using focus tracking as their primary mode wants the mouse to stop
        // *talking over* it, not to stop working.
        _hoveredElements
                    .DistinctUntilChanged(HoverElementIdentityComparer.Instance)
                    .Where(_ => _settings.Current.Accessibility.AnnounceOnHover)
                    .Throttle(TimeSpan.FromMilliseconds(
                        Math.Max(1, _settings.Current.Accessibility.HoverThrottleMs)))
                    .Select(info => Signal.FromAsync(async ct =>
                    {
                        // Every failure is swallowed here, per announcement, rather than being
                        // allowed to reach the subscription below. An Rx sequence that errors is
                        // *finished*, so letting one bad announcement through OnError silently
                        // ends hover speech for the rest of the session - which is exactly what
                        // happened when the breath animation threw a cross-thread exception:
                        // one throw, and Peek never spoke on hover again. The onError handler
                        // stays as a backstop, but nothing should ever reach it.
                        try
                        {
                            await _highlightService.StartBreathAsync(CancellationToken.None);
                            await SpeakElementAsync(info, ct);
                        }
                        catch (OperationCanceledException)
                        {
                            // Superseded by a newer hover - the normal case, not a failure.
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Announcing the hovered element failed");
                        }
                        finally
                        {
                            try { await _highlightService.StopBreathAsync(); }
                            catch (Exception ex) { _logger.LogDebug(ex, "Stopping the breath animation failed"); }
                        }
                        return RxVoid.Default;
                    }))
                    .Switch()
                    .Subscribe(
                        onNext: _ => { },
                        onError: ex => _logger.LogError(ex,
                            "Hover announcement pipeline terminated - hover speech is now dead for this session")
                    )
                    .DisposeWith(_disposables);

        // Cheap, always-on tracking of the raw cursor position - separate from the pipeline
        // above, which only ever carries a *resolved UIA element*. SpeakElementAsync reads this
        // for the first OCR scan of a newly-opaque window (see below for why a second position
        // has to come from somewhere else).
        _mouseTracker.MousePositionStream
            .Subscribe(p => _lastMousePosition = p)
            .DisposeWith(_disposables);

        // The OCR equivalent of the pipeline above, and deliberately NOT reusing it: an
        // OCR-opaque window (see OcrFallbackAnnouncer) resolves to the *same* UIA element
        // (Hwnd+Name+ControlType+Rect never change) no matter where the cursor is inside it, so
        // DistinctUntilChanged(ByIdentity) treats every subsequent position as a repeat and the
        // pipeline above never fires again after the first hover - which is exactly the "OCR
        // reads once but doesn't follow the mouse" gap this closes. Sourced from raw mouse
        // positions instead, settled the same way, and only ever does an in-memory lookup
        // against OcrFallbackAnnouncer's last cached scan (HasFreshScan/TryAnnounceForPositionAsync) -
        // never a fresh screenshot/OCR call - so it's cheap enough to run on every settled
        // position for as long as the window stays scanned.
        _mouseTracker.MousePositionStream
                    .Where(_ => _settings.Current.Accessibility.AnnounceOnHover)
                    .Throttle(TimeSpan.FromMilliseconds(
                        Math.Max(1, _settings.Current.Accessibility.HoverThrottleMs)))
                    .Select(point => Signal.FromAsync(async ct =>
                    {
                        try
                        {
                            var hwnd = CurrentElement?.Hwnd ?? 0;
                            if (hwnd != 0 && _ocrFallback.HasFreshScan(hwnd))
                                await _ocrFallback.TryAnnounceForPositionAsync(hwnd, point, SpeechPriority.Ambient, ct);
                        }
                        catch (OperationCanceledException)
                        {
                            // Superseded by a newer position - the normal case, not a failure.
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "OCR position-follow announcement failed");
                        }
                        return RxVoid.Default;
                    }))
                    .Switch()
                    .Subscribe(
                        onNext: _ => { },
                        onError: ex => _logger.LogError(ex,
                            "OCR position-follow pipeline terminated - OCR hover will no longer follow the mouse for this session")
                    )
                    .DisposeWith(_disposables);

        LogStep("Reactive subscriptions setup done");

        _logger.LogInformation("[Init] ElementTracker ctor finished");
    }
    private void LogStep(string step)
    {
        _sw.Stop();
        _logger.LogInformation("[Init] +{Elapsed}ms - {Step}",
            _sw.ElapsedMilliseconds,
            step);

        _sw.Restart();
    }
    public void Dispose()
    {
        // ElementTracker is a DI singleton that ScreenReaderViewModel both self-registers with
        // IDisposeService AND holds as a field disposed via its own Dispose() - the region
        // navigation library disposes the hosted view/viewmodel on its own when the shell
        // clears/switches regions, so this ends up called twice on ordinary app exit (tray
        // "Exit" -> MainViewModel.Dispose() -> _viewManager.Clear() disposes it once, then
        // _disposeService.DisposeAll() disposes it again). Without this guard the second call's
        // _hoveredElements.OnCompleted() throws ObjectDisposedException on the UI thread and
        // corrupts the shutdown sequence - see the crash reports with Context "UI thread" at
        // DisposeService.ExecuteCleanup / MainViewModel.Dispose / App.OnExit.
        if (_disposed) return;
        _disposed = true;

        _logger.LogDebug("Dispose ElementTracker");
        GC.SuppressFinalize(this);
        _disposables.Dispose();

        _hoveredElements.OnCompleted();
        _hoveredElements.Dispose();

        // Bounded, blocking wait rather than fire-and-forget: App.axaml.cs's OnExit
        // is synchronous with no async-shutdown hook, so without this the process
        // can exit before the worker is actually killed, leaving it orphaned.
        // ParentProcessWatchdog (Peek.Worker) is the backstop for the non-graceful
        // case (crash/force-kill); this is the fast path for a normal exit.
        try
        {
            _workerConnection.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanly stop Peek.Worker during shutdown");
        }
    }

    public async Task InitializeAsync()
    {
        await _workerConnection.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));

        // IsTracking has no persisted setting behind it - it's pure runtime state that always
        // starts false, so without this every single launch (not just the first one) left
        // Peek's core feature silently off until the user found the ON/OFF switch or already
        // knew Ctrl+Alt+T. For a screen-reading tool, hovering doing nothing by default was the
        // single biggest gap in an otherwise-working out-of-the-box experience. Set here (after
        // the worker's up), not in the field initializer, so it goes through the same
        // WhenAnyValue(IsTracking).Skip(1) pipeline as a real user toggle and doesn't fire
        // before there's a worker connection to query.
        IsTracking = true;

        _workerConnection.State
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(s => WorkerState = s)
            .DisposeWith(_disposables);

        _workerConnection
            .PollStatus(TimeSpan.FromSeconds(5))
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(status =>
            {
                _logger.LogDebug($"StatusCheck:{status}");
                if (status is not null)
                    StatusText = $"v{status.Version}  queries={status.QueriesServed}  " +
                                 $"cache={status.CacheHits}/{status.CacheHits + status.CacheMisses}";
            })
            .DisposeWith(_disposables);

    }

    /// <summary>
    /// "Is this the same control the user was already on?" - the question
    /// <c>DistinctUntilChanged</c> has to answer before deciding whether to speak again.
    /// </summary>
    /// <remarks>
    /// <see cref="SemanticElement"/> is a class with no equality of its own, and the worker
    /// returns a freshly deserialized instance for every query, so reference equality answers
    /// "always different" - which made the announcement stream re-fire for a stationary
    /// element. Position is deliberately part of the identity: two list rows can share a name
    /// and control type, and moving between them should speak.
    /// </remarks>
    /// <summary>The comparer above, exposed so its rules can be pinned by tests.</summary>
    public static IEqualityComparer<SemanticElement> HoverIdentityComparer =>
        HoverElementIdentityComparer.Instance;

    private sealed class HoverElementIdentityComparer : IEqualityComparer<SemanticElement>
    {
        public static readonly HoverElementIdentityComparer Instance = new();

        public bool Equals(SemanticElement? x, SemanticElement? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;

            // The worker's own cache key, when it has one, is the most reliable answer.
            if (!string.IsNullOrEmpty(x.ElementId) || !string.IsNullOrEmpty(y.ElementId))
                return x.ElementId == y.ElementId;

            return x.Hwnd == y.Hwnd
                && x.AutomationId == y.AutomationId
                && x.Name == y.Name
                && x.ControlType == y.ControlType
                && x.Rect.Left == y.Rect.Left
                && x.Rect.Top == y.Rect.Top
                && x.Rect.Width == y.Rect.Width
                && x.Rect.Height == y.Rect.Height;
        }

        public int GetHashCode(SemanticElement obj) =>
            string.IsNullOrEmpty(obj.ElementId)
                ? HashCode.Combine(obj.Hwnd, obj.AutomationId, obj.Name, obj.ControlType,
                    obj.Rect.Left, obj.Rect.Top, obj.Rect.Width, obj.Rect.Height)
                : obj.ElementId.GetHashCode();
    }
    /// <summary>
    /// Announces the new element via the structured speech pipeline (§10/§11);
    /// interruption of whatever is currently speaking is handled inside
    /// IAccessibilitySpeechService. Tries the OCR fallback first for a window UIA can't see
    /// into (see OcrFallbackAnnouncer/docs/OCR_STRATEGY.md) - only when that has nothing to
    /// add does this fall back to announcing the (possibly empty) UIA content directly.
    /// </summary>
    private async Task SpeakElementAsync(SemanticElement? info, CancellationToken ct)
    {
        if (info is null) return;

        _logger.LogDebug("Get element: {Name}; rect: {Rect}", info.Name, info.Rect);

        if (await _ocrFallback.TryAnnounceAsync(info, SpeechPriority.Ambient, _lastMousePosition, ct))
            return;

        await _speechService.AnnounceAsync(info, SpeechPriority.Ambient, ct);
    }
    private Task<RxVoid> StartTrackingCore()
    {
        // _trackingDisposable.IsDisposed only ever becomes true once (when the
        // slot itself is torn down, e.g. by _disposables.Dispose() on shutdown) -
        // it does NOT distinguish "never created" from "created and currently
        // active", since a freshly-constructed, still-empty slot also reports
        // IsDisposed == false. _trackingStarted is the actual "have we created the
        // subscription yet" flag; without it this branch always looked like
        // "already tracking, just resume" - including on the very first enable,
        // so the underlying TrackMouseElement subscription was never created at all.
        if (_trackingStarted)
        {
            _logger.LogInformation("Hover tracking resumed");
            _mouseTracker.Resume();
            _highlightService.Resume();
            return Task.FromResult(RxVoid.Default);
        }

        // Logged at Information, not Debug: "is tracking even on?" is the first question any
        // report of "hovering does nothing" has to answer, and it cannot be answered from a
        // default-level log otherwise - the toggle lives in the UI and leaves no other trace.
        _logger.LogInformation(
            "Hover tracking started (announce on hover: {Announce}, highlight follow: {Interval}ms, announce settle: {Settle}ms)",
            _settings.Current.Accessibility.AnnounceOnHover,
            HighlightFollowInterval.TotalMilliseconds,
            _settings.Current.Accessibility.HoverThrottleMs);

        // Resume on the first start too, not just on the resume path. It is what installs the
        // low-level mouse hook (WindowsMouseTracker only installs it on Resume, never at
        // construction - construction happens during startup, and a global low-level mouse
        // hook installed then would stutter the cursor desktop-wide), so without this the
        // click-to-select-text hook would never come up in a session that enables tracking once.
        _mouseTracker.Resume();

        _trackingStarted = true;
        var isFirst = true;

        // Deliberately NOT ObserveOn(MainThreadScheduler). This subscription used to hop to the
        // UI thread first, which quietly put *everything downstream of it* there too: setting
        // CurrentElement raises PropertyChanged, the announcement pipeline hangs off that, and
        // so the UIA round-trip, speech synthesis and audio playback all ended up running on
        // the thread that renders the highlight. Moving the box was then queued behind the
        // talking. Now the only UI-thread work on this path is the two marshalled calls below,
        // and the highlight goes first.
        _trackingDisposable.Create(_workerConnection
            .TrackMouseElement(
                _mouseTracker.MousePositionStream.Select(p => (p.X, p.Y)),
                followInterval: HighlightFollowInterval)
            .WhereIsNotNull()
            .Subscribe(info =>
            {
                if (isFirst)
                {
                    OnUiThread(_highlightService.Initialize);
                    isFirst = false;
                }

                // 1. The highlight, first and on this thread. IHighlightOverlay.Update posts to
                //    the dispatcher itself and never blocks, so this is the shortest path there
                //    is from "query answered" to "box moved" - and nothing below can delay it.
                //    Checked here (the fast, ~25ms-cadence callback) rather than in the
                //    throttled OCR position-follow subscription above: a synchronous, cache-only
                //    lookup is cheap enough to run on every sample, and this is what lets the box
                //    itself track individual lines of OCR text at UIA-hover responsiveness rather
                //    than lagging behind at the speech side's coarser settle cadence. Falls back
                //    to the whole element's rect - the pre-existing behaviour - whenever there's
                //    no fresh scan yet or the cursor isn't over any recognized line.
                var highlightRect = _ocrFallback.TryGetLineScreenRect(info.Hwnd, _lastMousePosition, out var lineRect)
                    ? lineRect
                    : new System.Drawing.Rectangle(info.Rect.Left, info.Rect.Top, info.Rect.Width, info.Rect.Height);
                _highlightService.UpdateLocation(highlightRect, info.Hwnd);

                // 2. Announcements, on a stream of their own (background, settle-debounced).
                //    Fed from here rather than derived from the CurrentElement property so
                //    speech never inherits the UI thread just because the details panel needs it.
                _hoveredElements.OnNext(info);

                // The highlight follows every one of these; an announcement may not
                // (AnnounceOnHover and the settle window gate it separately). Separating the
                // two in the log distinguishes "hover is dead" from "hover works but is quiet".
                _logger.LogDebug("Hover resolved: {Name} ({Type})", info.Name, info.ControlType);

                // 3. The UI-bound property last: it drives the Element Details panel, so it has
                //    to be on the UI thread, and it is the least latency-sensitive of the three.
                OnUiThread(() => CurrentElement = info);
            }));
        return Task.FromResult(RxVoid.Default);
    }

    private Task<RxVoid> StopTrackingCore()
    {
        _logger.LogInformation("Hover tracking stopped");
        _mouseTracker.Pause();
        CurrentElement = null;
        _highlightService.Hide();
        return Task.FromResult(RxVoid.Default);
    }
}
