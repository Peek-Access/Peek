using System.Runtime.InteropServices;
using System.Windows.Automation;
using Microsoft.Extensions.Logging;
using Peek.Worker.Contracts.Automation;

namespace Peek.Worker.Automation.Windows;

public sealed class WindowsAutomationService : IAutomationService, IDisposable
{
    private readonly ILogger<WindowsAutomationService> _logger;
    private readonly StaThreadDispatcher _dispatcher = new("peek-worker-uia");
    private readonly AutomationElementCache _cache = new();
    private readonly InspectorElementCache _inspectorCache = new();

    public WindowsAutomationService(ILogger<WindowsAutomationService> logger)
    {
        _logger = logger;
    }

    public Task<SemanticElement?> GetElementFromPointAsync(int x, int y, CancellationToken ct = default) =>
        _dispatcher.InvokeAsync(() =>
        {
            var cached = _cache.TryGetByPoint(x, y);
            if (cached is not null) return cached;

            var point = new System.Windows.Point(x, y);
            AutomationElement element;
            try
            {
                element = AutomationElement.FromPoint(point);
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or COMException)
            {
                _logger.LogDebug(ex, "No element at point ({X},{Y})", x, y);
                return null;
            }

            element = ResolveAnnouncementElement(element, point);

            var hwnd = NativeMethods.GetWindowHandleAt(x, y);
            var semantic = SafeCreateSemantic(element, hwnd);
            if (semantic is null) return null;

            _cache.PutByPoint(x, y, semantic);
            return semantic;
        }, ct);

    public Task<SemanticElement?> GetElementFromHandleAsync(nint hwnd, CancellationToken ct = default) =>
        _dispatcher.InvokeAsync(() =>
        {
            var cached = _cache.TryGetByHandle(hwnd);
            if (cached is not null) return cached;

            var element = TryGetElementFromHandle(hwnd);
            if (element is null) return null;

            var semantic = SafeCreateSemantic(element, hwnd);
            if (semantic is null) return null;

            _cache.PutByHandle(hwnd, semantic);
            return semantic;
        }, ct);

    public Task<SemanticElement?> GetFocusedElementAsync(CancellationToken ct = default) =>
        _dispatcher.InvokeAsync(() =>
        {
            AutomationElement? element;
            try
            {
                element = AutomationElement.FocusedElement;
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or COMException)
            {
                // Normal during focus handoff - the element that had focus can already be
                // gone by the time this call lands (closing dialog, app exiting).
                _logger.LogDebug(ex, "Focused element was unavailable");
                return null;
            }

            if (element is null) return null;

            // NativeWindowHandle is 0 for elements that aren't themselves HWNDs (most
            // controls in WPF/UWP/Chromium trees) - fall back to the containing
            // top-level window so the caller still gets something addressable.
            nint hwnd = 0;
            try
            {
                hwnd = element.Current.NativeWindowHandle;
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or COMException)
            {
                _logger.LogDebug(ex, "Could not read the focused element's window handle");
            }

            return SafeCreateSemantic(element, hwnd);
        }, ct);

    public Task<IReadOnlyList<SemanticElement>> GetChildrenAsync(nint hwnd, int depth = 0, CancellationToken ct = default) =>
        _dispatcher.InvokeAsync(() =>
        {
            var root = TryGetElementFromHandle(hwnd);
            if (root is null) return (IReadOnlyList<SemanticElement>)Array.Empty<SemanticElement>();

            var results = new List<SemanticElement>();
            CollectChildren(root, hwnd, depth, results, ct);
            return (IReadOnlyList<SemanticElement>)results;
        }, ct);

    public Task ClearCacheAsync(CancellationToken ct = default) =>
        _dispatcher.InvokeAsync(() => _cache.Clear(), ct);

    public Task<SemanticElement?> GetInspectorRootAsync(nint hwnd, CancellationToken ct = default) =>
        _dispatcher.InvokeAsync(() =>
        {
            var element = TryGetElementFromHandle(hwnd);
            if (element is null) return null;

            var id = _inspectorCache.Put(element, hwnd);
            return SafeCreateSemantic(element, hwnd, id);
        }, ct);

    /// <summary>
    /// A window (or any element in its subtree) can be closed by the user at any moment
    /// while they're mid-browse in the Inspector - GetFirstChild/GetNextSibling on a now-
    /// dead element throws, and even a child TreeWalker just returned can die before its
    /// properties are read. Both are handled defensively here (SafeWalk / SafeCreateSemantic)
    /// so a element dying mid-walk skips that element/stops the walk rather than failing
    /// the whole RPC call.
    /// </summary>
    public Task<IReadOnlyList<SemanticElement>> GetInspectorChildrenAsync(string elementId, CancellationToken ct = default) =>
        _dispatcher.InvokeAsync(() =>
        {
            if (_inspectorCache.TryGet(elementId) is not { } parent)
                return (IReadOnlyList<SemanticElement>)Array.Empty<SemanticElement>();

            var results = new List<SemanticElement>();
            var walker = TreeWalker.ControlViewWalker;
            var child = SafeWalk(() => walker.GetFirstChild(parent.Element));

            while (child is not null)
            {
                ct.ThrowIfCancellationRequested();

                var childId = _inspectorCache.Put(child, parent.Hwnd);
                var semantic = SafeCreateSemantic(child, parent.Hwnd, childId);
                if (semantic is not null)
                    results.Add(semantic);

                child = SafeWalk(() => walker.GetNextSibling(child));
            }

            return (IReadOnlyList<SemanticElement>)results;
        }, ct);

    public Task ResetInspectorAsync(CancellationToken ct = default) =>
        _dispatcher.InvokeAsync(() => _inspectorCache.Clear(), ct);

    public Task<SemanticElement?> RefreshInspectorElementAsync(string elementId, CancellationToken ct = default) =>
        _dispatcher.InvokeAsync(() =>
        {
            if (_inspectorCache.TryGet(elementId) is not { } entry)
                return null;

            return SafeCreateSemantic(entry.Element, entry.Hwnd, elementId);
        }, ct);

    public AutomationDiagnostics GetDiagnostics() => new(_cache.Hits, _cache.Misses);

    private AutomationElement? TryGetElementFromHandle(nint hwnd)
    {
        try
        {
            return AutomationElement.FromHandle(hwnd);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or ArgumentException)
        {
            _logger.LogDebug(ex, "No element for handle 0x{Hwnd:X}", hwnd);
            return null;
        }
    }

    private void CollectChildren(
        AutomationElement parent,
        nint hwnd,
        int remainingDepth,
        List<SemanticElement> results,
        CancellationToken ct)
    {
        var walker = TreeWalker.ControlViewWalker;
        var child = SafeWalk(() => walker.GetFirstChild(parent));

        while (child is not null)
        {
            ct.ThrowIfCancellationRequested();

            var semantic = SafeCreateSemantic(child, hwnd);
            if (semantic is not null)
                results.Add(semantic);

            if (remainingDepth > 0)
                CollectChildren(child, hwnd, remainingDepth - 1, results, ct);

            child = SafeWalk(() => walker.GetNextSibling(child));
        }
    }

    /// <summary>
    /// UI Automation hit-testing (AutomationElement.FromPoint) returns the topmost/most
    /// specific element at the point, but many real controls put their actual visible text
    /// on a CHILD element sitting exactly inside that outer container's bounds rather than on
    /// the container itself: Win32/WPF edit boxes are usually fine (ValuePattern lives on the
    /// hit element directly), but plenty of XAML (WinUI/UWP, and the Windows 11 shell's own
    /// search boxes), Electron/Chromium (rich contenteditable areas), and other custom-drawn
    /// controls expose an inner "text"/"static" node - overlapping the outer Edit/Group's own
    /// rect - as the only place the literal string lives, while the outer node's own
    /// Name/Value stay empty. Left alone, that reads as "Edit" with nothing to announce.
    ///
    /// This only fires when the hit element itself has nothing worth speaking (the common
    /// case - a real Value/Name on the hit element itself - costs nothing extra). From there
    /// it walks down through children whose bounding rect still contains the same point
    /// (so it can't wander onto an unrelated sibling elsewhere in the container, e.g. a
    /// clear/reveal-password button next to the text), stopping as soon as one has real text,
    /// or after a few bounded hops if none do - each hop is a handful of cross-process UIA
    /// property reads, so this is deliberately kept shallow and narrow rather than a full
    /// subtree scan (which, run on every hover, would be the kind of latency this app can't
    /// afford - see WorkerRpcChannel's mouse-tracking notes).
    /// </summary>
    private static AutomationElement ResolveAnnouncementElement(AutomationElement hit, System.Windows.Point point)
    {
        const int maxDepth = 3;
        const int maxChildrenPerLevel = 8;

        if (HasMeaningfulText(hit)) return hit;

        var current = hit;
        for (var depth = 0; depth < maxDepth; depth++)
        {
            var candidate = FindPointContainingChild(current, point, maxChildrenPerLevel);
            if (candidate is null) break;
            if (HasMeaningfulText(candidate)) return candidate;
            current = candidate;
        }

        return hit;
    }

    /// <summary>First direct child (control view) whose own bounding rect still contains
    /// <paramref name="point"/>, preferring one that already has meaningful text over one
    /// that doesn't (so a single pass can both find a drill-down target AND recognize when
    /// it's already the answer).</summary>
    private static AutomationElement? FindPointContainingChild(AutomationElement parent, System.Windows.Point point, int maxChildrenToScan)
    {
        var walker = TreeWalker.ControlViewWalker;
        var child = SafeWalk(() => walker.GetFirstChild(parent));

        AutomationElement? firstMatchWithoutText = null;
        var scanned = 0;
        while (child is not null && scanned < maxChildrenToScan)
        {
            scanned++;

            var rect = SemanticElementFactory.SafeGet(() => child.Current.BoundingRectangle);
            if (rect is { IsEmpty: false } r && r.Contains(point))
            {
                if (HasMeaningfulText(child)) return child;
                firstMatchWithoutText ??= child;
            }

            child = SafeWalk(() => walker.GetNextSibling(child));
        }

        return firstMatchWithoutText;
    }

    private static bool HasMeaningfulText(AutomationElement element)
    {
        var name = SemanticElementFactory.SafeGet(() => element.Current.Name);
        if (!string.IsNullOrWhiteSpace(name)) return true;

        if (SemanticElementFactory.TryGetPattern<ValuePattern>(element, ValuePattern.Pattern, out var valuePattern))
        {
            var value = SemanticElementFactory.SafeGet(() => valuePattern!.Current.Value);
            if (!string.IsNullOrWhiteSpace(value)) return true;
        }

        return false;
    }

    private static AutomationElement? SafeWalk(Func<AutomationElement> walk)
    {
        try
        {
            return walk();
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// SemanticElementFactory.Create reads a snapshot of the element's live properties
    /// (element.Current, patterns, ...) - any of that can throw if the element (or its
    /// owning window) is destroyed in the instant between TreeWalker handing it back and
    /// these reads happening. Null here means "skip this element", not an error.
    /// </summary>
    private SemanticElement? SafeCreateSemantic(AutomationElement element, nint fallbackHwnd, string? elementId = null)
    {
        try
        {
            return SemanticElementFactory.Create(element, fallbackHwnd, elementId);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException)
        {
            _logger.LogDebug(ex, "Element became unavailable while reading its properties (closed mid-browse?)");
            return null;
        }
    }

    public void Dispose() => _dispatcher.Dispose();
}
