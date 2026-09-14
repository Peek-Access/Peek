using System.Runtime.Versioning;
using ReactiveUI.Primitives.Signals;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Peek.Core.Services;

public sealed class FocusChangedArgs : EventArgs
{
    public IntPtr WindowHandle { get; init; }
    public uint ProcessId { get; init; }
}

/// <summary>
/// Raises an event whenever keyboard focus moves anywhere on the desktop
/// (EVENT_OBJECT_FOCUS), which <see cref="FocusAnnouncer"/> turns into speech. This is
/// the counterpart to <see cref="WindowsMouseTracker"/>: hover tracking serves people who
/// can still aim a mouse, focus tracking serves people who navigate by keyboard because
/// they can't see where to aim.
/// </summary>
/// <remarks>
/// Only the raw OS event is produced here - resolving what actually has focus is a worker
/// RPC round-trip (automation.getFocusedElement) and is deliberately left to the consumer,
/// so a burst of focus events can be throttled down to one query rather than one per event.
/// </remarks>
[SupportedOSPlatform("windows7.0")]
public sealed class FocusTracker : IDisposable
{
    private const uint EVENT_OBJECT_FOCUS = 0x8005;

    // Focus events also fire for pseudo-objects that live on the same HWND but aren't
    // "the user moved focus to something" - the caret blinking, the mouse cursor, a
    // scrollbar, a sound. Those are excluded by name; everything else is accepted.
    //
    // Deliberately a denylist, not an allowlist of {OBJID_WINDOW, OBJID_CLIENT}: real apps
    // report a focused item using idObject values well outside those two - observed live,
    // a terminal reports successive focused items as plain positive child ids (31, 32, 33…),
    // and an allowlist silently dropped every one of them, so focus tracking appeared to do
    // nothing at all in exactly the apps it most needs to work in. What actually has focus
    // is resolved through UIA (automation.getFocusedElement) rather than from this id, so
    // the id only ever needs to be good enough to reject noise.
    private const int OBJID_VSCROLL = unchecked((int)0xFFFFFFFB);  // -5
    private const int OBJID_HSCROLL = unchecked((int)0xFFFFFFFA);  // -6
    private const int OBJID_SIZEGRIP = unchecked((int)0xFFFFFFF9); // -7
    private const int OBJID_CARET = unchecked((int)0xFFFFFFF8);    // -8
    private const int OBJID_CURSOR = unchecked((int)0xFFFFFFF7);   // -9
    private const int OBJID_ALERT = unchecked((int)0xFFFFFFF6);    // -10
    private const int OBJID_SOUND = unchecked((int)0xFFFFFFF5);    // -11

    private readonly WindowsHookService _hookService;
    private readonly Signal<FocusChangedArgs> _focusChanged = new();
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private int _disposed;

    /// <summary>Emits every accepted focus change. Never completes until disposed.</summary>
    public IObservable<FocusChangedArgs> FocusChanged => _focusChanged;

    public FocusTracker(WindowsHookService hookService)
    {
        _hookService = hookService;
        _hookService.RegisterWinEvent(EVENT_OBJECT_FOCUS, EVENT_OBJECT_FOCUS);
        _hookService.WinEventFired += OnWinEvent;
    }

    private void OnWinEvent(object? sender, WinEventArgs e)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return;

        if (e.EventType != EVENT_OBJECT_FOCUS || e.WindowHandle == IntPtr.Zero)
            return;

        if (IsNoiseObject(e.ObjectId))
            return;

        PInvoke.GetWindowThreadProcessId((HWND)e.WindowHandle, out var pid);

        // Peek's own UI must never announce itself: every announcement moves focus inside
        // Peek (or at least can), which would feed straight back into another announcement.
        if (pid == _ownProcessId)
            return;

        _focusChanged.OnNext(new FocusChangedArgs
        {
            WindowHandle = e.WindowHandle,
            ProcessId = pid,
        });
    }

    private static bool IsNoiseObject(int objectId) => objectId
        is OBJID_CARET or OBJID_CURSOR or OBJID_ALERT or OBJID_SOUND
        or OBJID_HSCROLL or OBJID_VSCROLL or OBJID_SIZEGRIP;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _hookService.WinEventFired -= OnWinEvent;
        _focusChanged.OnCompleted();
        _focusChanged.Dispose();
    }
}
