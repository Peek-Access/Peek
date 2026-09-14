using System.Collections.Concurrent;
using System.Drawing;
using Microsoft.Extensions.Logging;
using Peek.Core.Services.Speech;
using Peek.Core.Settings;
using Peek.Ipc.Worker;
using Peek.Worker.Contracts.Automation;
using Peek.Worker.Contracts.Ocr;

namespace Peek.Core.Services.Ocr;

/// <summary>
/// The piece that turns <see cref="IOcrDecisionService"/> from a fully-tested but unreachable
/// state machine into something that actually runs: called by <see cref="ElementTracker"/> and
/// <see cref="FocusAnnouncer"/> before they fall back to announcing an element's (empty) UIA
/// content, for a window like a chat app's message list - one custom-drawn surface with no
/// per-message UIA content at all, so hovering/focusing it used to announce only the window
/// itself and nothing of what's actually on screen. See docs/OCR_STRATEGY.md.
/// </summary>
/// <remarks>
/// The hard part isn't running OCR, it's deciding *when UIA has actually failed* rather than
/// "this one hovered pixel happened to land on an unlabeled element" (an icon button, a blank
/// spacer - both completely normal in an otherwise fully-accessible app, where OCR would just be
/// noise). Two different signals feed that decision, deliberately not just one:
/// <list type="bullet">
/// <item>The <em>hovered element itself</em> has no name/value - cheap, checked on every
/// hover/focus, and specifically does NOT count a bare top-level Window's title as content (see
/// <see cref="HasMeaningfulContent"/>) - every real window has a title, so counting it would
/// make this true for exactly the case that matters: an app whose whole client area is one
/// undifferentiated Window element with nothing UIA can drill into (confirmed against WeChat -
/// every hover position resolves to the same "Weixin, Window" element).</item>
/// <item>The <em>window as a whole</em> exposes no meaningful descendants at all
/// (<see cref="IsWindowUiaOpaqueAsync"/>) - a one-time-per-window check (cached), since "does
/// this app have real UIA content anywhere" doesn't change hover to hover. This is what lets an
/// app that's merely thin *at this one pixel* be told apart from one that's opaque everywhere,
/// without needing every such app hand-listed in <see cref="OcrSettings.KnownProblematicApplications"/> -
/// that list is now only a fast-path/trusted-automatic-run shortcut, not a hard requirement.</item>
/// </list>
/// </remarks>
public sealed class OcrFallbackAnnouncer
{
    /// <summary>
    /// How many extra levels below the immediate children to probe when checking whether a
    /// window exposes any real UIA content. Deep enough to reach past a couple of layout
    /// containers in a normally-structured app; shallow enough to keep the one-time-per-window
    /// probe (see <see cref="_opacityCache"/>) cheap even against a large tree.
    /// </summary>
    private const int OpacityProbeDepth = 2;

    /// <summary>
    /// How long a window's "does it expose real UIA content" verdict is trusted before
    /// re-checking - long enough that ordinary hovering never repeats the probe, short enough
    /// that a window whose content genuinely loads in after the first check (e.g. a slow app
    /// still populating its UI when first hovered) doesn't stay misjudged for the rest of the
    /// session.
    /// </summary>
    private static readonly TimeSpan OpacityCacheTtl = TimeSpan.FromSeconds(30);

    private readonly IOcrDecisionService _decisionService;
    private readonly WorkerConnection _workerConnection;
    private readonly WindowEnumerator _windowEnumerator;
    private readonly IAccessibilitySpeechService _speechService;
    private readonly ISettingsService _settings;
    private readonly ILogger<OcrFallbackAnnouncer> _logger;

    /// <summary>
    /// How long a cached screenshot/OCR scan is trusted for position-based line lookups
    /// (<see cref="TryAnnounceForPositionAsync"/>) before it's considered too stale to serve -
    /// at that point the caller falls silent rather than reading text that may no longer match
    /// what's on screen, until the next hover-driven <see cref="RunAndAnnounceAsync"/> refreshes
    /// it. Position lookups themselves never trigger a re-scan - they're the cheap, frequent
    /// path a mouse-follow subscription can call on every settled position without cost.
    /// </summary>
    private static readonly TimeSpan ScanCacheTtl = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<nint, (bool IsOpaque, DateTime CheckedAtUtc)> _opacityCache = new();

    // Keyed by window, not by element: the throttle is "has this window's visible content
    // likely changed", not "have we scanned this exact sub-element before".
    private readonly ConcurrentDictionary<nint, DateTime> _lastRunByWindow = new();

    /// <summary>
    /// The most recent screenshot+OCR result per window, plus the screen rect it was captured
    /// against (needed to turn a screen point back into an image-relative one) - what lets
    /// <see cref="TryAnnounceForPositionAsync"/> answer "what's under the cursor right now"
    /// without re-running OCR on every mouse movement.
    /// </summary>
    private readonly ConcurrentDictionary<nint, CachedScan> _scanCache = new();

    /// <summary>The line index last announced per window, so re-settling on the same line doesn't repeat it (mirrors ElementTracker's own hover de-duplication, but keyed by OCR line instead of UIA element identity, which never changes across an opaque window).</summary>
    private readonly ConcurrentDictionary<nint, int> _lastAnnouncedLineIndex = new();

    private sealed record CachedScan(OcrResult Result, Rectangle WindowRect, DateTime CapturedAtUtc);

    public OcrFallbackAnnouncer(
        IOcrDecisionService decisionService,
        WorkerConnection workerConnection,
        WindowEnumerator windowEnumerator,
        IAccessibilitySpeechService speechService,
        ISettingsService settings,
        ILogger<OcrFallbackAnnouncer> logger)
    {
        _decisionService = decisionService;
        _workerConnection = workerConnection;
        _windowEnumerator = windowEnumerator;
        _speechService = speechService;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Speaks OCR-recognized text in place of <paramref name="element"/>'s UIA content when the
    /// decision engine says to. Returns true if it spoke something (including a "more may be
    /// available" suggestion) - callers should skip their own plain element announcement then.
    /// </summary>
    /// <param name="hoverPoint">
    /// The screen point that triggered this (e.g. the current mouse position for a hover-driven
    /// call). When given and it lands on a recognized line, only that line is spoken - the whole
    /// window's text otherwise (there's no meaningful "position" for a keyboard-focus-driven
    /// call, so <see cref="FocusAnnouncer"/> passes null).
    /// </param>
    public async Task<bool> TryAnnounceAsync(SemanticElement element, SpeechPriority priority, Point? hoverPoint, CancellationToken ct)
    {
        var ocr = _settings.Current.Ocr;
        if (ocr.Preference == OcrUserPreference.Disabled) return false;
        if (element.Hwnd == 0) return false;
        if (HasMeaningfulContent(element)) return false;

        var processName = element.ProcessId > 0 ? _windowEnumerator.GetProcessName(element.ProcessId) : string.Empty;
        var isKnownProblematic = IsKnownProblematicApplication(processName, ocr.KnownProblematicApplications);

        // Known apps skip the probe entirely (already trusted opaque); anything else has to
        // prove it - a single unlabeled element is not enough evidence on its own. Logged at
        // Information (not Debug, the app's default level) specifically so "why didn't this
        // read anything" is diagnosable from a stock log without needing PEEK_LOG_LEVEL=Debug -
        // this line is the one place that answers it, for every element that even reaches
        // this far (i.e. one UIA itself already had nothing to say about).
        var isOpaque = isKnownProblematic || await IsWindowUiaOpaqueAsync(element.Hwnd, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "OCR fallback: hwnd={Hwnd:X} process='{Process}' controlType={ControlType} knownProblematic={KnownProblematic} windowOpaque={Opaque}",
            element.Hwnd, processName, element.ControlType, isKnownProblematic, isOpaque);
        if (!isOpaque) return false;

        var lastRun = _lastRunByWindow.TryGetValue(element.Hwnd, out var t) ? t : (DateTime?)null;
        var decision = _decisionService.Decide(new OcrDecisionContext
        {
            AutomationAvailable = true,
            AutomationHasMeaningfulContent = false,
            // Deliberately isOpaque, not isKnownProblematic: DefaultOcrDecisionService's own
            // rule only lets IsKnownProblematicApplication=true run OCR automatically (anything
            // else only gets a suggestion, on the theory that a single thin element is weak
            // evidence). A window that just failed the whole-tree opacity probe is *stronger*
            // evidence than a name match - it's inspected, not guessed - so it deserves at
            // least the same trust. This is what makes KnownProblematicApplications an optional
            // fast-path (skip the probe for names already known) rather than a hard requirement
            // for automatic reading to work at all - an app nobody has ever hand-listed still
            // gets read automatically the first time it's confirmed opaque.
            IsKnownProblematicApplication = isOpaque,
            UserPreference = ocr.Preference,
            IsSpeaking = _speechService.IsChannelReserved,
            TimeSinceLastOcr = lastRun is null ? null : DateTime.UtcNow - lastRun.Value,
        });

        _logger.LogInformation("OCR fallback decision for hwnd={Hwnd:X}: {Decision}", element.Hwnd, decision);

        switch (decision)
        {
            case OcrDecision.RunAutomatically:
                return await RunAndAnnounceAsync(element, priority, hoverPoint, ct).ConfigureAwait(false);

            case OcrDecision.SuggestOcr:
                var message = ocr.SuggestionMessage ?? OcrSuggestionMessages.Default;
                await _speechService.AnnounceTextAsync(message, priority, ct).ConfigureAwait(false);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// True once a window has demonstrated it has nothing for UI Automation to drill into
    /// anywhere - not just at the point the cursor happens to be over.
    /// </summary>
    private async Task<bool> IsWindowUiaOpaqueAsync(nint hwnd, CancellationToken ct)
    {
        if (_opacityCache.TryGetValue(hwnd, out var cached) && DateTime.UtcNow - cached.CheckedAtUtc < OpacityCacheTtl)
            return cached.IsOpaque;

        bool opaque;
        try
        {
            var descendants = await _workerConnection.Client.Automation
                .GetChildrenAsync(hwnd, OpacityProbeDepth, ct).ConfigureAwait(false);
            var candidates = descendants
                .Where(d => !IsStandardWindowChrome(d) && !IsStructuralContainer(d.ControlType))
                .ToList();
            opaque = !candidates.Any(HasMeaningfulContent);

            // Temporary, elevated to Information: pins down exactly which descendant a window
            // gets judged non-opaque by, rather than guessing at what a given app's automation
            // tree looks like from the outside. Drop back to Debug (or remove) once this stops
            // being needed for that.
            if (!opaque)
                _logger.LogInformation(
                    "Opacity probe for hwnd={Hwnd:X}: not opaque because of: {Details}",
                    hwnd,
                    string.Join(" | ", candidates.Where(HasMeaningfulContent)
                        .Select(d => $"[{d.ControlType}] Name='{d.Name}' Value='{d.Value}' AutomationId='{d.AutomationId}'")));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fail closed: a probe that couldn't complete is not evidence of opacity, so don't
            // start running OCR off the back of it.
            _logger.LogWarning(ex, "Could not determine whether hwnd={Hwnd:X} exposes any UIA content", hwnd);
            opaque = false;
        }

        _opacityCache[hwnd] = (opaque, DateTime.UtcNow);
        return opaque;
    }

    private async Task<bool> RunAndAnnounceAsync(SemanticElement element, SpeechPriority priority, Point? hoverPoint, CancellationToken ct)
    {
        // Recorded before the call, not after: a slow/failed OCR attempt must still count as
        // "just tried" so a rapid string of hovers over the same unresponsive window doesn't
        // fire off a screenshot+OCR round trip for every single one of them.
        _lastRunByWindow[element.Hwnd] = DateTime.UtcNow;

        try
        {
            // The screenshot+OCR round trip below is the one genuinely slow step in this whole
            // path (typically a few hundred ms, sometimes more on a large/high-DPI window) - said
            // up front so a screen-reader user gets *something* immediately instead of silence
            // that's indistinguishable from "nothing is going to happen". Superseded automatically
            // by the real announcement below the moment it's ready (same "newer speech cancels
            // older" rule every other announcement already relies on) - no special handling needed
            // for the fast-OCR case where this gets cut off almost immediately.
            await _speechService.AnnounceTextAsync(FormatExtractingMessage(element.Name), priority, ct).ConfigureAwait(false);

            var screenshot = await _workerConnection.Client.Screenshot.CaptureWindowAsync(element.Hwnd, ct).ConfigureAwait(false);
            var result = await _workerConnection.Client.Ocr.RecognizeAsync(screenshot.ImageData, ct: ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(result.Text)) return false;

            var windowRect = new Rectangle(element.Rect.Left, element.Rect.Top, element.Rect.Width, element.Rect.Height);
            _scanCache[element.Hwnd] = new CachedScan(result, windowRect, DateTime.UtcNow);

            var lineIndex = hoverPoint is { } p ? FindLineAt(result, windowRect, p) : -1;
            var text = lineIndex >= 0 ? result.Lines[lineIndex].Text : result.Text;
            _lastAnnouncedLineIndex[element.Hwnd] = lineIndex;

            await _speechService.AnnounceTextAsync(text, priority, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Automatic OCR fallback failed");
            return false;
        }
    }

    /// <summary>
    /// The cheap, frequent counterpart to <see cref="RunAndAnnounceAsync"/>: called on every
    /// settled mouse position while hovering a window already confirmed opaque and scanned, so
    /// moving to a different line of on-screen text is announced the way moving to a different
    /// UIA element normally would be - no screenshot, no OCR, just a lookup against the last
    /// cached scan. Returns false (does nothing) if there's no fresh-enough scan to check
    /// against yet, or the point isn't over any recognized line and nothing was previously
    /// announced for this window - the hover-driven path (<see cref="TryAnnounceAsync"/>) owns
    /// producing the first scan.
    /// </summary>
    public async Task<bool> TryAnnounceForPositionAsync(nint hwnd, Point screenPoint, SpeechPriority priority, CancellationToken ct)
    {
        if (!_scanCache.TryGetValue(hwnd, out var scan) || DateTime.UtcNow - scan.CapturedAtUtc > ScanCacheTtl)
            return false;

        var lineIndex = FindLineAt(scan.Result, scan.WindowRect, screenPoint);

        var lastIndex = _lastAnnouncedLineIndex.TryGetValue(hwnd, out var last) ? last : -2;
        if (lineIndex == lastIndex) return false;

        _lastAnnouncedLineIndex[hwnd] = lineIndex;
        if (lineIndex < 0) return false; // moved off every recognized line - fall silent, don't repeat the last one

        await _speechService.AnnounceTextAsync(scan.Result.Lines[lineIndex].Text, priority, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Whether <paramref name="hwnd"/> has a scan fresh enough for <see cref="TryAnnounceForPositionAsync"/> to act on - lets a caller skip the position-lookup subscription entirely for every other window.</summary>
    public bool HasFreshScan(nint hwnd) =>
        _scanCache.TryGetValue(hwnd, out var scan) && DateTime.UtcNow - scan.CapturedAtUtc <= ScanCacheTtl;

    /// <summary>-1 if the point isn't inside any recognized line's bounding box (the axis-aligned bounds of its quad - OCR text boxes are near-axis-aligned for standard on-screen UI, so a full point-in-polygon test isn't worth the complexity). The first matching line wins on overlap.</summary>
    private static int FindLineAt(OcrResult result, Rectangle windowRect, Point screenPoint)
    {
        var x = screenPoint.X - windowRect.Left;
        var y = screenPoint.Y - windowRect.Top;

        for (var i = 0; i < result.Lines.Count; i++)
        {
            if (GetLineBounds(result.Lines[i]).Contains(x, y))
                return i;
        }

        return -1;
    }

    /// <summary>The axis-aligned bounding box of an OCR line's quad, in image-relative coordinates.</summary>
    private static Rectangle GetLineBounds(OcrLine line)
    {
        var box = line.Box;
        var minX = Math.Min(Math.Min(box.X1, box.X2), Math.Min(box.X3, box.X4));
        var maxX = Math.Max(Math.Max(box.X1, box.X2), Math.Max(box.X3, box.X4));
        var minY = Math.Min(Math.Min(box.Y1, box.Y2), Math.Min(box.Y3, box.Y4));
        var maxY = Math.Max(Math.Max(box.Y1, box.Y2), Math.Max(box.Y3, box.Y4));

        return Rectangle.FromLTRB((int)minX, (int)minY, (int)Math.Ceiling(maxX), (int)Math.Ceiling(maxY));
    }

    /// <summary>
    /// The synchronous, cache-only counterpart to <see cref="TryAnnounceForPositionAsync"/> for
    /// the highlight box rather than speech: called from the existing high-frequency (~25ms)
    /// per-mouse-sample highlight callback in <see cref="ElementTracker"/>, so the outline
    /// tracks individual lines of recognized text at the same responsiveness UIA hover already
    /// has - not the coarser, throttled cadence the speech side settles for.
    /// </summary>
    /// <param name="lineRect">
    /// The matched line's bounding box in screen coordinates, inflated by one pixel in each of
    /// width and height (an OCR quad's bounds hug the glyphs tightly enough that an outline drawn
    /// at the exact same size can visually clip the text it's supposed to be framing).
    /// </param>
    /// <returns>False if there's no fresh scan for <paramref name="hwnd"/> or the point isn't over any recognized line - callers should fall back to outlining the whole element/window.</returns>
    public bool TryGetLineScreenRect(nint hwnd, Point screenPoint, out Rectangle lineRect)
    {
        lineRect = default;
        if (!_scanCache.TryGetValue(hwnd, out var scan) || DateTime.UtcNow - scan.CapturedAtUtc > ScanCacheTtl)
            return false;

        var lineIndex = FindLineAt(scan.Result, scan.WindowRect, screenPoint);
        if (lineIndex < 0) return false;

        var bounds = GetLineBounds(scan.Result.Lines[lineIndex]);
        lineRect = new Rectangle(
            scan.WindowRect.Left + bounds.Left,
            scan.WindowRect.Top + bounds.Top,
            bounds.Width + 1,
            bounds.Height + 1);
        return true;
    }

    /// <summary>Exposed for tests. Kept short and generic on purpose - spoken while the user is mid-hover, ahead of whatever the OCR result turns out to say.</summary>
    public static string FormatExtractingMessage(string? windowName) =>
        string.IsNullOrWhiteSpace(windowName)
            ? "Extracting text, one moment..."
            : $"Extracting text from {windowName}, one moment...";

    /// <summary>
    /// Exposed for tests. A top-level Window's own Name is its title bar text, not "content" -
    /// every real window has one (see remarks above), so counting it would defeat the one case
    /// this whole class exists for.
    /// </summary>
    public static bool HasMeaningfulContent(SemanticElement element)
    {
        if (element.ControlType == "Window") return false;
        return !string.IsNullOrWhiteSpace(element.Name) || !string.IsNullOrWhiteSpace(element.Value);
    }

    /// <summary>Exposed for tests. Case-insensitive: process name casing isn't a meaningful signal.</summary>
    public static bool IsKnownProblematicApplication(string processName, IReadOnlyList<string> knownProblematicApplications) =>
        !string.IsNullOrEmpty(processName)
        && knownProblematicApplications.Contains(processName, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> CaptionButtonNames =
        new(StringComparer.OrdinalIgnoreCase) { "Minimize", "Maximize", "Close", "Restore" };

    /// <summary>
    /// Exposed for tests. Every window - UIA-opaque or not - has a title bar, a system menu,
    /// and minimize/maximize/close buttons, and UIA reports all of them with real names
    /// ("Minimize", "System Menu Bar", the window's own title, ...). Counting those as
    /// "content" made the opacity probe in <see cref="IsWindowUiaOpaqueAsync"/> report every
    /// window as non-opaque, chrome included, regardless of whether the app exposed anything
    /// past it - confirmed against a real window with zero content controls, whose only
    /// descendants were exactly this set. "Context help"/AutomationId "Help" is the same kind
    /// of OS-provided title-bar button (the WS_EX_CONTEXTHELP "?" button) - confirmed against
    /// Weixin's real window, which has one.
    /// </summary>
    public static bool IsStandardWindowChrome(SemanticElement element) =>
        element.ControlType is "TitleBar" or "MenuBar"
        || (element.ControlType == "MenuItem" && element.Name == "System")
        || (element.ControlType == "Button" && CaptionButtonNames.Contains(element.Name))
        || (element.ControlType == "Button" && element.AutomationId == "Help");

    private static readonly HashSet<string> StructuralContainerControlTypes = new(StringComparer.Ordinal)
    {
        "Pane", "Group", "Custom", "Window", "ToolBar", "ScrollBar", "Separator", "Thumb",
        "List", "Tree", "Table", "AppBar", "SemanticZoom", "ProgressBar",
    };

    /// <summary>
    /// Exposed for tests. Pure layout/structural UIA control types - real readable content
    /// lives on their children (Text, Edit, Button, ListItem, ...), never on the container
    /// itself, but the container still gets a Name from somewhere (often internal/technical:
    /// confirmed against Weixin's real window, whose descendants included a Pane named
    /// "MMUIRenderSubWindowHW" - a rendering-surface class name, not user-visible text - and
    /// another Pane whose Name just duplicated the window's own title). Without this, any such
    /// container made the opacity probe conclude the window has real content when it doesn't.
    /// </summary>
    public static bool IsStructuralContainer(string controlType) =>
        StructuralContainerControlTypes.Contains(controlType);
}
