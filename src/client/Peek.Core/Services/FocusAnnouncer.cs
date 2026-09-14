using System.Runtime.Versioning;
using Peek.Worker.Contracts.Automation;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using Microsoft.Extensions.Logging;
using Peek.Core.Abstractions;
using Peek.Core.Services.Ocr;
using Peek.Core.Services.Speech;
using Peek.Core.Settings;
using Peek.Ipc.Connection;
using Peek.Ipc.Worker;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;
using ReactiveUI.Primitives.Extensions;
using ReactiveUI.Primitives.Signals;

namespace Peek.Core.Services;

/// <summary>
/// Turns "keyboard focus moved" (<see cref="FocusTracker"/>) into a spoken announcement of
/// whatever now has focus - the follow-the-keyboard model a screen-reader user actually
/// navigates with, complementing the existing follow-the-mouse model in
/// <see cref="ElementTracker"/>.
/// </summary>
/// <remarks>
/// Gated live by <see cref="AccessibilitySettings.AnnounceOnFocus"/>, re-read on every event
/// rather than cached, so toggling it in Settings takes effect immediately (same convention
/// as <see cref="WindowAnnouncer"/>).
/// <para>
/// Focus events arrive in bursts - opening a dialog can move focus several times within a few
/// dozen milliseconds - so they're throttled and then <c>Switch()</c>ed: only the newest
/// query survives, and an in-flight query for focus the user has already moved on from is
/// cancelled rather than spoken late. Without that, fast Tab-Tab-Tab navigation would queue
/// up a backlog of stale announcements that lag further behind with every keypress.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows7.0")]
public sealed class FocusAnnouncer : IDisposable
{
    private readonly MultipleDisposable _disposables = [];
    private readonly WorkerConnection _workerConnection;
    private readonly ElementTracker _elementTracker;
    private readonly IAccessibilitySpeechService _speechService;
    private readonly OcrFallbackAnnouncer _ocrFallback;
    private readonly IHighlightService _highlightService;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    private readonly ISettingsService _settings;
    private readonly ILogger<FocusAnnouncer> _logger;

    public FocusAnnouncer(
        FocusTracker focusTracker,
        WorkerConnection workerConnection,
        ElementTracker elementTracker,
        IAccessibilitySpeechService speechService,
        OcrFallbackAnnouncer ocrFallback,
        IHighlightService highlightService,
        ISettingsService settings,
        ILogger<FocusAnnouncer> logger)
    {
        _workerConnection = workerConnection;
        _elementTracker = elementTracker;
        _speechService = speechService;
        _ocrFallback = ocrFallback;
        _highlightService = highlightService;
        _settings = settings;
        _logger = logger;

        focusTracker.FocusChanged
            .Where(_ => _settings.Current.Accessibility.AnnounceOnFocus)
            .Throttle(TimeSpan.FromMilliseconds(Math.Max(1, _settings.Current.Accessibility.FocusThrottleMs)))
            .Select(args => Signal.FromAsync(ct => AnnounceFocusedAsync(args, ct)))
            .Switch()
            .Subscribe(
                onNext: _ => { },
                onError: ex => _logger.LogError(ex, "Focus announcement pipeline error"))
            .DisposeWith(_disposables);
    }

    private async Task<RxVoid> AnnounceFocusedAsync(FocusChangedArgs args, CancellationToken ct)
    {
        // One switch governs the whole screen reader, not just the half of it that follows
        // the mouse. Following the keyboard is the same feature seen from the other side -
        // it announces and outlines whatever takes focus - and leaving it always-on made the
        // app's behaviour incoherent from the outside: with the toggle reading OFF, Peek
        // still spoke and still drew boxes, including around windows the user had merely
        // activated from another page. AnnounceOnFocus and HighlightFocusedElement remain
        // settings for *what* the screen reader does; this is the switch for *whether* it is
        // running at all.
        if (!_elementTracker.IsTracking)
            return RxVoid.Default;

        // The worker owns UI Automation; if it's restarting there's nothing to ask yet, and
        // a focus change is not worth queueing until it comes back - by then the user has
        // moved on.
        if (_workerConnection.CurrentState != ConnectionState.Ready)
            return RxVoid.Default;

        try
        {
            var element = await _workerConnection.Client.Automation
                .GetFocusedElementAsync(ct)
                .ConfigureAwait(false);

            if (element is null || ct.IsCancellationRequested)
                return RxVoid.Default;

            // Peek must never describe or outline itself. FocusTracker already drops focus
            // events raised by this process, but that is not sufficient and the difference is
            // what users saw: this call asks the worker for whatever holds focus *now*, which
            // is a fresh system-wide lookup, not the element the event carried. At startup the
            // sequence is another window losing focus, then Peek's own window taking it, so the
            // lookup resolves to Peek - and a highlight box the exact size of Peek's window
            // appeared pinned to its edge, in a session where the user had switched nothing on.
            if (element.ProcessId == _ownProcessId)
            {
                _logger.LogDebug("Focused element belongs to Peek itself - not announcing or highlighting it");
                return RxVoid.Default;
            }

            if (IsAnonymousFullScreenContainer(element))
            {
                _logger.LogDebug(
                    "Focused element is an unnamed full-screen container ({Type}, {Width}x{Height}) - skipping",
                    element.ControlType, element.Rect.Width, element.Rect.Height);
                return RxVoid.Default;
            }

            if (_settings.Current.Accessibility.HighlightFocusedElement && element.Rect.Width > 0 && element.Rect.Height > 0)
            {
                // Logged because "why is there a highlight box I didn't ask for?" was not
                // answerable from the log at all - the focus highlight is a separate setting
                // from the visible Tracking toggle, so it can legitimately draw while that
                // toggle reads OFF, and telling that apart from a bug needs this line.
                _logger.LogDebug("Highlighting focused element: {Name} ({Type}) at {Left},{Top} {Width}x{Height} pid={Pid}",
                    element.Name, element.ControlType,
                    element.Rect.Left, element.Rect.Top, element.Rect.Width, element.Rect.Height,
                    element.ProcessId);

                _highlightService.Show(
                    new System.Drawing.Rectangle(element.Rect.Left, element.Rect.Top, element.Rect.Width, element.Rect.Height),
                    args.WindowHandle);
            }

            if (!await _ocrFallback.TryAnnounceAsync(element, SpeechPriority.Ambient, hoverPoint: null, ct).ConfigureAwait(false))
                await _speechService.AnnounceAsync(element, SpeechPriority.Ambient, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer focus change - expected during fast keyboard navigation.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not announce the focused element");
        }

        return RxVoid.Default;
    }

    /// <summary>
    /// Fraction of the desktop an element has to cover before it counts as "the whole screen"
    /// for <see cref="IsAnonymousFullScreenContainer"/>.
    /// </summary>
    private const double FullScreenAreaFraction = 0.7;

    /// <summary>
    /// True for the nameless, screen-filling container elements that some apps hand back as the
    /// focused element - and which are worth neither outlining nor saying.
    /// </summary>
    /// <remarks>
    /// This is what produced the mystery box: on a fresh start, with nothing switched on that
    /// the user could see, an unnamed 1500x1032 Pane belonging to whatever app sat behind Peek
    /// (WhatsApp, in the captured case) took focus and got outlined - a rectangle covering the
    /// entire desktop beside Peek's docked panel, which reads as "a weird highlight box stuck to
    /// Peek" rather than as a highlight of anything. Spoken, it is just as useless: "pane".
    /// <para>
    /// Deliberately requires the name to be empty. A maximised window with a real name is the
    /// same size and absolutely should be announced ("Notepad, window") - it is the anonymity
    /// plus the size that makes an element carry no information.
    /// </para>
    /// </remarks>
    private bool IsAnonymousFullScreenContainer(SemanticElement element) =>
        IsAnonymousFullScreenContainer(
            element,
            PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXVIRTUALSCREEN),
            PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYVIRTUALSCREEN));

    /// <summary>The decision itself, separated from reading the screen metrics so it can be tested.</summary>
    public static bool IsAnonymousFullScreenContainer(SemanticElement element, int screenWidth, int screenHeight)
    {
        if (!string.IsNullOrWhiteSpace(element.Name)) return false;
        if (screenWidth <= 0 || screenHeight <= 0) return false;

        var screenArea = (double)screenWidth * screenHeight;
        var elementArea = (double)element.Rect.Width * element.Rect.Height;

        return elementArea >= screenArea * FullScreenAreaFraction;
    }

    public void Dispose() => _disposables.Dispose();
}
