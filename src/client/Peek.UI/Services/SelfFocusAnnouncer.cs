using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Peek.Core.Services.Speech;
using Peek.Core.Settings;
using Peek.Ipc.Connection;
using Peek.Ipc.Worker;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;
using AvaloniaToggleState = Avalonia.Automation.Provider.ToggleState;
using PeekSemanticElement = Peek.Worker.Contracts.Automation.SemanticElement;
using PeekToggleState = Peek.Worker.Contracts.Automation.ToggleState;
using PeekExpandCollapseState = Peek.Worker.Contracts.Automation.ExpandCollapseState;

namespace Peek.UI.Services;

/// <summary>
/// Announces whatever takes keyboard focus inside Peek's own windows - the same
/// follow-the-keyboard behavior <see cref="Peek.Core.Services.FocusAnnouncer"/> gives every
/// other application, extended to Peek itself. A screen reader that cannot read its own
/// Settings screen is not usable by the audience it exists for.
/// </summary>
/// <remarks>
/// FocusTracker/FocusAnnouncer both deliberately exclude Peek's own process, because that
/// pipeline resolves focus through a raw OS event stream and a worker-side UI Automation
/// query - broad enough to also pick up incidental OS-level focus churn an announcement
/// could cause. This class avoids that failure mode at the source by listening to Avalonia's
/// own managed focus event instead: <see cref="InputElement.GotFocusEvent"/> only fires for
/// a genuine Tab-stop or click target, and nothing in Peek's own announcement views ever
/// calls <c>Focus()</c> on itself, so there is no path back into a second announcement.
/// </remarks>
[SupportedOSPlatform("windows7.0")]
public sealed class SelfFocusAnnouncer : IDisposable
{
    private readonly WorkerConnection _workerConnection;
    private readonly IAccessibilitySpeechService _speechService;
    private readonly ISettingsService _settings;
    private readonly ILogger<SelfFocusAnnouncer> _logger;
    private readonly Signal<Control> _focusChanged = new();
    private readonly IDisposable _subscription;
    private int _disposed;

    public SelfFocusAnnouncer(
        WorkerConnection workerConnection,
        IAccessibilitySpeechService speechService,
        ISettingsService settings,
        ILogger<SelfFocusAnnouncer> logger)
    {
        _workerConnection = workerConnection;
        _speechService = speechService;
        _settings = settings;
        _logger = logger;

        // A class handler registered against TopLevel fires once for every window Peek ever
        // opens (MainWindow, AboutWindow, dialogs, ...) without wiring each one individually -
        // GotFocusEvent bubbles from whatever control was actually focused up to its owning
        // TopLevel, where this catches it with e.Source still pointing at that control.
        InputElement.GotFocusEvent.AddClassHandler<TopLevel>(OnGotFocus, handledEventsToo: true);

        _subscription = _focusChanged
            .Throttle(TimeSpan.FromMilliseconds(Math.Max(1, _settings.Current.Accessibility.FocusThrottleMs)))
            // Throttle's default scheduler is not the UI thread, but Describe() below reads
            // the focused Control's AutomationPeer, which - unlike everything FocusAnnouncer
            // touches - is a live Avalonia object with UI-thread affinity. Without this hop
            // back, ControlAutomationPeer.CreatePeerForElement throws
            // "The calling thread cannot access this object because a different thread owns it."
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Select(control => Signal.FromAsync(ct => AnnounceAsync(control, ct)))
            .Switch()
            .Subscribe(
                onNext: _ => { },
                onError: ex => _logger.LogError(ex, "Self-focus announcement pipeline error"));
    }

    private void OnGotFocus(TopLevel topLevel, FocusChangedEventArgs e)
    {
        if (Volatile.Read(ref _disposed) == 1) return;
        if (!_settings.Current.Accessibility.AnnounceOwnInterface) return;
        if (e.NewFocusedElement is Control control) _focusChanged.OnNext(control);
    }

    private async Task<RxVoid> AnnounceAsync(Control control, CancellationToken ct)
    {
        // Same reasoning as FocusAnnouncer: nothing to speak through yet, and by the time the
        // worker reconnects the user has already tabbed on to something else.
        if (_workerConnection.CurrentState != ConnectionState.Ready)
            return RxVoid.Default;

        try
        {
            // .ObserveOn(RxSchedulers.MainThreadScheduler) in the constructor is meant to
            // guarantee this call is already on the UI thread - confirmed (via
            // Peek.UI.Tests.SelfFocusAnnouncerTests, an Avalonia.Headless test) that it does
            // not reliably do so, so this checks explicitly rather than trusting it: a
            // cross-thread call into ControlAutomationPeer.CreatePeerForElement throws.
            var element = Dispatcher.UIThread.CheckAccess()
                ? Describe(control)
                : await Dispatcher.UIThread.InvokeAsync(() => Describe(control));

            if (element is not null)
                await _speechService.AnnounceAsync(element, SpeechPriority.Ambient, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer focus change - expected during fast Tab navigation.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not announce Peek's own focused element");
        }

        return RxVoid.Default;
    }

    /// <summary>
    /// Reads whatever Peek's own control already exposes as its automation peer - the exact
    /// same accessible name/role/state a real screen reader would see through Windows UI
    /// Automation - and repackages it as the same SemanticElement shape the external-app
    /// pipeline uses, so it flows through the identical <see cref="ISpeechPolicy"/> formatting
    /// ("Login, button") rather than a second, divergent phrasing.
    /// </summary>
    private static PeekSemanticElement? Describe(Control control)
    {
        if (ControlAutomationPeer.CreatePeerForElement(control) is not { } peer)
            return null;

        return new PeekSemanticElement
        {
            Name = peer.GetName() ?? string.Empty,
            ControlType = peer.GetAutomationControlType().ToString(),
            ProcessId = (uint)Environment.ProcessId,
            IsEnabled = peer.IsEnabled(),
            IsKeyboardFocusable = true,
            IsFocused = true,
            Value = peer.GetProvider<IValueProvider>()?.Value,
            IsSelected = peer.GetProvider<ISelectionItemProvider>()?.IsSelected,
            ToggleState = peer.GetProvider<IToggleProvider>() is { } toggle ? MapToggleState(toggle.ToggleState) : null,
            ExpandState = peer.GetProvider<IExpandCollapseProvider>() is { } expand ? MapExpandState(expand.ExpandCollapseState) : null,
            AcceleratorKey = NullIfEmpty(peer.GetAcceleratorKey()),
            AccessKey = NullIfEmpty(peer.GetAccessKey()),
            HelpText = NullIfEmpty(peer.GetHelpText()),
        };
    }

    // Both enums mirror the same UIA-standard member names (Avalonia's automation model is
    // deliberately shaped after UI Automation's), so matching by name rather than a hardcoded
    // switch keeps this working if either side ever adds a member the other doesn't expect.
    private static PeekToggleState? MapToggleState(AvaloniaToggleState state) =>
        Enum.TryParse<PeekToggleState>(state.ToString(), out var mapped) ? mapped : null;

    private static PeekExpandCollapseState? MapExpandState(ExpandCollapseState state) =>
        Enum.TryParse<PeekExpandCollapseState>(state.ToString(), out var mapped) ? mapped : null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        _subscription.Dispose();
        _focusChanged.OnCompleted();
        _focusChanged.Dispose();
    }
}
