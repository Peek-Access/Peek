using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Peek.Worker.Contracts.Automation;
using Peek.Core.Abstractions;
using Peek.Core.Services;
using Peek.Core.Services.Llm;
using Peek.Core.Services.Speech;
using Peek.Core.Settings;
using Peek.Ipc.Worker;
using ReactiveUI.SourceGenerators;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;

namespace Peek.Core.ViewModels;

/// <summary>
/// A UI-tree browser (à la FlaUI Inspector / Inspect.exe) - one of the four monitor views
/// (see MainViewModel/DockShellViewModel), hosted either as a MainWindow nav tab or a dock
/// strip page depending on DockShellSettings.Mode. Root rows are top-level windows (cheap
/// client-side Win32 enumeration via WindowTracker); expanding a row lazily fetches its UIA
/// automation children from the worker - a UIA tree can be huge, so nothing beyond the
/// window list is ever fetched eagerly. All UIA access goes through
/// WorkerConnection.Client.Automation, never direct client-side UI Automation calls.
/// </summary>
/// <remarks>
/// Selecting any row also announces it (name, type, owning process, live position/size -
/// see InspectorSelectionAnnouncementFormatter), brings its window forward if minimized
/// or backgrounded, and highlights it - which is what makes this genuinely usable by a
/// reading-impaired user exploring an unfamiliar app's UI, not just a debugging tool.
/// </remarks>
public partial class ElementInspectorViewModel : ViewModelBase, IDisposable
{
    private readonly WindowEnumerator _windowEnumerator;
    private readonly WorkerConnection _workerConnection;
    private readonly IAccessibilitySpeechService _speechService;
    private readonly ILogger<ElementInspectorViewModel> _logger;
    private readonly ISettingsService _settingsService;

    /// <summary>Spoken announcements follow the TTS language, not the UI language - see SpeechStrings.</summary>
    private CultureInfo SpeechCulture => SpeechStrings.ResolveCulture(_settingsService.Current.Localization);

    /// <summary>Read once at construction - the view's code-behind parses this into a local key handler when it attaches, see ElementInspectorView.</summary>
    public string ExpandShortcutGesture { get; }

    public ObservableCollection<InspectorNodeViewModel> Roots { get; } = [];

    public ScreenAnalysisService ScreenAnalysis { get; }

    [Reactive]
    private InspectorNodeViewModel? _selectedNode;

    /// <summary>True while the top-level window list is being enumerated - see LoadRootsAsync.</summary>
    [Reactive]
    private bool _isLoading;

    /// <summary>
    /// Set while <see cref="LoadRootsAsync"/> refills <see cref="Roots"/>, so the selection the
    /// DataGrid makes on its own in response is not mistaken for one the user made.
    /// </summary>
    private bool _suppressSelectionSideEffects;

    public ElementInspectorViewModel(IServiceProvider serviceProvider, ILogger<ElementInspectorViewModel> logger)
    {
        // Timed because "the first switch to this page freezes for seconds" has to be
        // attributable to something: this constructor, the window enumeration it kicks off, or
        // the view's own first inflation. Without all three on the clock, the answer is a guess.
        var ctorStopwatch = Stopwatch.StartNew();

        _windowEnumerator = serviceProvider.GetRequiredService<WindowEnumerator>();
        _workerConnection = serviceProvider.GetRequiredService<WorkerConnection>();
        _speechService = serviceProvider.GetRequiredService<IAccessibilitySpeechService>();
        ScreenAnalysis = serviceProvider.GetRequiredService<ScreenAnalysisService>();
        _logger = logger;

        _settingsService = serviceProvider.GetRequiredService<ISettingsService>();
        ExpandShortcutGesture = _settingsService.Current.Keyboard.Shortcuts
            .GetValueOrDefault("InspectorToggleExpand", "Space");

        var disposeService = serviceProvider.GetRequiredService<IDisposeService>();
        disposeService.Register(this);

        _ = LoadRootsAsync();

        // Plain PropertyChanged rather than WhenAnyValue(...).Subscribe(...): this app's
        // custom ReactiveUI.Primitives library makes that Subscribe(Action<T>) overload
        // ambiguous with System.Reactive's (see the tray-icon fix in App.axaml.cs for the
        // same issue) - sidestepping it entirely is simpler than chasing the right using.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SelectedNode) && SelectedNode is { } node)
                _ = OnNodeSelectedAsync(node);
        };

        _logger.LogDebug("ElementInspectorViewModel constructed in {Elapsed}ms", ctorStopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Frees the worker's Inspector session cache (entries are explicit-reset, not TTL-evicted - see InspectorElementCache, so
    /// a long-running worker would otherwise accumulate every element from every past
    /// session). Registered with IDisposeService rather than any per-navigation lifecycle
    /// hook: with dock-region caching (PreferCache) this view can outlive many
    /// navigations-away, so "the app is shutting down" is the only point that actually
    /// means "done with this Inspector session".
    /// </summary>
    public void Dispose()
    {
        // Fire-and-forget, not awaited: IDisposable.Dispose is synchronous, and blocking on
        // this at shutdown risks the exact dispatcher deadlock this codebase already avoids
        // elsewhere (see the settings-load comment in App.axaml.cs). Best-effort cleanup -
        // worst case the worker's cache lingers a little longer, not a correctness issue.
        _ = _workerConnection.Client.Automation.ResetInspectorAsync()
            .ContinueWith(t => _logger.LogWarning(t.Exception, "Failed to reset the worker's Inspector cache on shutdown"),
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }

    [ReactiveCommand]
    private Task Refresh() => LoadRootsAsync();

    /// <summary>
    /// Sends the selected row's window to the AI for narrated analysis - see
    /// ScreenAnalysisService. Enabled for any selected node (a window row or a descendant
    /// element), always analyzing the row's owning window via PrintWindow (see
    /// InspectorNodeViewModel.Hwnd), not just window rows. Resolves the window's title/
    /// process fresh via WindowEnumerator rather than trusting the selected node's own
    /// Name/ProcessName - for a descendant-element row those describe the *element*, not
    /// its owning window, which would mislabel the analysis.
    /// </summary>
    [ReactiveCommand]
    private Task AnalyzeSelectedWindow()
    {
        if (SelectedNode is not { } node || node.Hwnd == 0) return Task.CompletedTask;

        var snapshot = _windowEnumerator.SnapshotSingle(node.Hwnd);
        return ScreenAnalysis.AnalyzeWindowAsync(node.Hwnd, snapshot?.Title, snapshot?.ProcessName);
    }

    [ReactiveCommand]
    private void StopAnalysis() => ScreenAnalysis.Cancel();

    /// <summary>
    /// Expands a node (loading its children from the worker if not already cached) and
    /// announces how many it turned out to have - the single entry point both a mouse
    /// click on the expand arrow (ElementInspectorView.OnNodeExpanded) and the
    /// configurable keyboard shortcut (ToggleExpandSelected) go through, so the
    /// announcement behaves identically either way.
    /// </summary>
    public async Task ExpandNodeAsync(InspectorNodeViewModel node)
    {
        // See InspectorNodeViewModel.IsExpanding: setting IsExpanded below can
        // synchronously re-enter this exact method before it ever awaits anything, via the
        // DataGrid's own NodeExpanded event reacting to the property change.
        if (node.IsExpanding) return;

        node.IsExpanding = true;
        try
        {
            node.IsExpanded = true;
            await LoadChildrenAsync(node);

            try
            {
                await _speechService.AnnounceTextAsync(InspectorChildCountAnnouncementFormatter.Format(node.Children.Count, SpeechCulture), SpeechPriority.UserRequested);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to announce child count for {Name}", node.Name);
            }
        }
        finally
        {
            node.IsExpanding = false;
        }
    }

    [ReactiveCommand]
    private async Task ToggleExpandSelected()
    {
        if (SelectedNode is not { } node) return;

        if (node.IsExpanded)
            node.IsExpanded = false;
        else
            await ExpandNodeAsync(node);
    }

    /// <summary>
    /// Fills the top-level window list, off the UI thread.
    /// </summary>
    /// <remarks>
    /// Must run off the UI thread: the enumeration below is hundreds of windows times eight
    /// P/Invokes each, and running it on the thread that has to draw this page would freeze
    /// the window for seconds on first navigation.
    /// <para>
    /// Deliberately restricted to top-level windows only. <c>EnumerateAll()</c> defaults to
    /// <c>includeChildren: true</c>, which would walk the entire child-window tree of every
    /// top-level window on the desktop - thousands of HWNDs - only for the <c>IsRoot</c> filter
    /// here to throw every one of them away. Children are fetched lazily per node from the
    /// worker when a row is expanded (see LoadChildrenAsync); nothing here ever wants them.
    /// </para>
    /// </remarks>
    private async Task LoadRootsAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Raw HWND enumeration turns up hundreds of windows a user never asked to see -
            // invisible IME/tooltip/helper windows (MSCTFIME UI, tooltips_class32,
            // ThumbnailDeviceHelperWnd, ...) that exist as real HWNDs but were never meant to
            // be end-user-visible. Restrict roots to windows a person could actually point at:
            // visible, and occupying real screen space. Also excludes Peek's own windows (by
            // process id, not a single HWND - covers MainWindow plus any of About/ElementDetail/
            // the highlight overlay that happen to be open) - inspecting Peek's own UI here isn't
            // a real use case and just clutters the list.
            var currentProcessId = (uint)Environment.ProcessId;

            // Two clocks on purpose: a single clock spanning the await would charge the
            // enumeration for however long the UI thread took to pick the continuation back
            // up - and on a first navigation the UI thread is busy inflating this very page,
            // which would make a ~30ms enumeration read as ~500ms of "slow loading" and point
            // optimisation at entirely the wrong target.
            var enumerationMs = 0L;

            var windows = await Task.Run(() =>
            {
                var inner = Stopwatch.StartNew();
                var result = _windowEnumerator.EnumerateVisibleRoots(excludeProcessId: currentProcessId);
                enumerationMs = inner.ElapsedMilliseconds;
                return result;
            }).ConfigureAwait(true);

            _suppressSelectionSideEffects = true;
            try
            {
                Roots.Clear();
                foreach (var window in windows)
                    Roots.Add(new InspectorNodeViewModel(window));
            }
            finally
            {
                _suppressSelectionSideEffects = false;
            }

            _logger.LogDebug(
                "Inspector roots loaded: {Count} windows - enumeration {Enumeration}ms, total {Total}ms (difference is time waiting for the UI thread)",
                windows.Count, enumerationMs, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate top-level windows");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Lazily fetches and populates one node's direct children - called by
    /// ElementInspectorView's code-behind when the user expands a row. A no-op if
    /// already loaded or a load is already in flight.
    /// </summary>
    /// <remarks>
    /// Deliberately no ConfigureAwait(false) anywhere in this method: every await here is
    /// followed by mutating node.Children (an ObservableCollection bound into the
    /// DataGrid's HierarchicalModel) or a [Reactive] property, both of which Avalonia's
    /// dispatcher requires to happen on the UI thread - continuing on a thread-pool thread
    /// throws "the calling thread cannot access this object because a different thread
    /// owns it" the moment the collection is touched. Catching and only logging that
    /// exception would make expanding a node silently show nothing: the RPC calls succeed,
    /// the UI update crashes invisibly.
    /// </remarks>
    public async Task LoadChildrenAsync(InspectorNodeViewModel node)
    {
        if (node.ChildrenLoaded || node.IsLoading) return;

        node.IsLoading = true;
        try
        {
            var automation = _workerConnection.Client.Automation;

            IReadOnlyList<SemanticElement> children;
            if (node.Window is not null)
            {
                var root = await automation.GetInspectorRootAsync(node.Hwnd);
                children = root?.ElementId is { } rootId
                    ? await automation.GetInspectorChildrenAsync(rootId)
                    : [];
            }
            else if (node.ElementId is { } elementId)
            {
                children = await automation.GetInspectorChildrenAsync(elementId);
            }
            else
            {
                children = [];
            }

            foreach (var child in children)
            {
                var processName = child.ProcessId > 0 ? _windowEnumerator.GetProcessName(child.ProcessId) : string.Empty;
                node.Children.Add(new InspectorNodeViewModel(child, node.Hwnd, processName));
            }

            node.MarkChildrenLoaded();
        }
        catch (Exception ex)
        {
            // Routine, not exceptional: the window/element this node represents can be
            // closed by the user at any moment while they're mid-browse, and the worker
            // surfaces that as an RPC failure (see the defensive handling in
            // WindowsAutomationService's Inspector methods) rather than ever crashing.
            // MarkLoadFailed (not MarkChildrenLoaded) leaves this node retry-able instead
            // of silently looking like a confirmed-empty leaf.
            _logger.LogWarning(ex, "Failed to load Inspector children for {Name}", node.Name);
            node.MarkLoadFailed();
        }
        finally
        {
            node.IsLoading = false;
        }
    }

    /// <summary>
    /// Resolves the full, freshly-refreshed SemanticElement behind a node for the
    /// double-click detail popup - see RefreshElementAsync for why this always re-reads
    /// live data instead of trusting a node's originally-fetched snapshot.
    /// </summary>
    public Task<SemanticElement?> ResolveElementAsync(InspectorNodeViewModel node) => RefreshElementAsync(node);

    /// <summary>
    /// The announce+highlight+bring-to-front pipeline that makes selecting a row in the
    /// Inspector genuinely usable by a reading-impaired user, not just a debugging aid:
    /// speaks what the selected thing is/is called/belongs to/is positioned at, while
    /// visually locating it - restoring and foregrounding its window first if needed, so
    /// the highlight isn't drawn over whatever else happens to be on top.
    /// </summary>
    private async Task OnNodeSelectedAsync(InspectorNodeViewModel node)
    {
        // Only the "did the user actually do this?" gate remains. Selecting a row activating
        // and announcing its window is this page's own job - it is not the screen reader
        // following the mouse, and gating it on that toggle (as a previous fix did) took
        // activation away with it. The list refilling re-selects a row on its own, though,
        // and acting on that would foreground a window nobody asked about.
        if (_suppressSelectionSideEffects)
        {
            _logger.LogDebug("Row selected while repopulating the list - ignoring");
            return;
        }

        try
        {
            // Logged because activating the selected window is this page's whole point and it
            // leaves no other trace - if this silently stopped firing, the only symptom would
            // be a row that visibly does nothing when clicked.
            var activated = _windowEnumerator.EnsureWindowForeground(node.Hwnd);
            _logger.LogDebug("Row selected: {Name} (hwnd 0x{Hwnd:X}) - {Activated}",
                node.Name, node.Hwnd, activated ? "activated its window" : "window already in front");

            if (activated)
            {
                // Give the restore/activate animation a moment to settle - UI Automation's
                // BoundingRectangle for a just-restored window can briefly report stale or
                // transitional bounds otherwise, which would highlight the wrong spot.
                await Task.Delay(TimeSpan.FromMilliseconds(150));
            }

            var element = await RefreshElementAsync(node);
            if (element is null) return;

            var processName = element.ProcessId > 0
                ? _windowEnumerator.GetProcessName(element.ProcessId)
                : node.ProcessName;

            // No highlight box here on purpose. Activating the window is what locates it for
            // the user, and outlining it as well conflated two different features - the
            // highlight belongs to the screen reader, which has its own switch.
            var rect = new System.Drawing.Rectangle(element.Rect.Left, element.Rect.Top, element.Rect.Width, element.Rect.Height);

            var controlTypeLabel = node.Window is not null ? "Window" : element.ControlType;
            var text = InspectorSelectionAnnouncementFormatter.Format(
                element.Name, controlTypeLabel, processName, rect.X, rect.Y, rect.Width, rect.Height, SpeechCulture);

            await _speechService.AnnounceTextAsync(text, SpeechPriority.UserRequested);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to announce/highlight the selected node {Name}", node.Name);
        }
    }

    /// <summary>
    /// Always re-fetches live data (position, size, state, ...) rather than reusing a
    /// node's originally-fetched snapshot, which can be stale by the time it's selected -
    /// the window may have moved, resized, or (window rows specifically) never had its
    /// own automation element fetched at all yet.
    /// </summary>
    private async Task<SemanticElement?> RefreshElementAsync(InspectorNodeViewModel node)
    {
        try
        {
            var automation = _workerConnection.Client.Automation;

            if (node.Window is not null)
                return await automation.GetInspectorRootAsync(node.Hwnd);

            if (node.ElementId is { } elementId)
                return await automation.RefreshInspectorElementAsync(elementId);

            return node.Element;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh live data for {Name}", node.Name);
            return null;
        }
    }
}
