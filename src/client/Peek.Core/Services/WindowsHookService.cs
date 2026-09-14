using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Peek.Core.Services;

public sealed class WindowsHookEventArgs : EventArgs
{
    /// <summary>
    /// Which hook produced this. <see cref="WindowsHookService.HookFired"/> is one event shared
    /// by every installed hook, so without this every mouse event also ran the keyboard
    /// subscribers and vice versa - each of them re-deriving "is this mine?" from wParam values
    /// that happen not to collide.
    /// </summary>
    public WINDOWS_HOOK_ID HookId { get; init; }

    public int NCode { get; init; }
    public nuint WParam { get; init; }
    public nint LParam { get; init; }
}

public sealed class WinEventArgs : EventArgs
{
    public uint EventType { get; init; }
    public IntPtr WindowHandle { get; init; }
    public int ObjectId { get; init; }
    public int ChildId { get; init; }
    public uint EventThread { get; init; }
    public uint EventTimeMs { get; init; }
}

[SupportedOSPlatform("windows6.0")]
public sealed class WindowsHookService : IDisposable
{

    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    private readonly record struct LowLevelHookEntry(
        WINDOWS_HOOK_ID HookId,
        HOOKPROC Delegate,
        UnhookWindowsHookExSafeHandle Handle);

    private readonly record struct WinEventEntry(
        WINEVENTPROC Delegate,
        HWINEVENTHOOK Handle);

    private readonly List<LowLevelHookEntry> _llHooks = [];
    private readonly List<WinEventEntry> _winEvents = [];

    private int _disposed;

    private readonly ILogger<WindowsHookService> _logger;

    public WindowsHookService(ILogger<WindowsHookService> logger)
    {
        _logger = logger;
    }

    public event EventHandler<WindowsHookEventArgs>? HookFired;

    public event EventHandler<WinEventArgs>? WinEventFired;

    /// <summary>
    /// Installs a global low-level hook. Call this as late as possible and
    /// <see cref="UnregisterLowLevelHook"/> as early as possible - see the remarks, this is
    /// not a cheap thing to leave running.
    /// </summary>
    /// <remarks>
    /// A WH_MOUSE_LL/WH_KEYBOARD_LL procedure runs on the thread that installed it, and
    /// Windows delivers <em>every input event on the machine</em> through it before any
    /// application sees it. If that thread is busy, input for the whole desktop stalls until
    /// the procedure returns or LowLevelHooksTimeout (300ms by default) expires - for every
    /// event. Installing a mouse hook from the UI thread during startup therefore made the
    /// cursor stutter system-wide for the 10-15 seconds Peek took to initialise, which is an
    /// especially bad first impression for a tool a user may be relying on to navigate.
    /// <para>
    /// Two rules follow, and both are load-bearing: install only while the feature that needs
    /// it is actually on, and keep <see cref="LowLevelCallback"/> allocation-free and
    /// branch-cheap.
    /// </para>
    /// </remarks>
    public void RegisterLowLevelHook(WINDOWS_HOOK_ID hookId)
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);

        lock (_llHooks)
        {
            if (_llHooks.Any(e => e.HookId == hookId)) return;

            HOOKPROC proc = (nCode, wParam, lParam) =>
                LowLevelCallback(hookId, nCode, wParam, lParam);

            using var module = System.Diagnostics.Process.GetCurrentProcess().MainModule!;
            var hMod = PInvoke.GetModuleHandle(module.ModuleName);
            var handle = PInvoke.SetWindowsHookEx(hookId, proc, hMod, 0);

            if (handle.IsInvalid)
                throw new InvalidOperationException(
                    $"SetWindowsHookEx({hookId}) failed: 0x{Marshal.GetLastWin32Error():X8}");

            _llHooks.Add(new LowLevelHookEntry(hookId, proc, handle));
        }

        // Information, not Debug: a global low-level hook charges latency to every application
        // on the machine, so when one exists - and for how long - has to be answerable from a
        // default-level log rather than inferred.
        _logger.LogInformation("Installed global low-level hook {HookId}", hookId);
    }

    /// <summary>Removes a hook installed by <see cref="RegisterLowLevelHook"/>. Safe to call when it isn't installed.</summary>
    public void UnregisterLowLevelHook(WINDOWS_HOOK_ID hookId)
    {
        lock (_llHooks)
        {
            var index = _llHooks.FindIndex(e => e.HookId == hookId);
            if (index < 0) return;

            var entry = _llHooks[index];
            _llHooks.RemoveAt(index);

            if (!entry.Handle.IsInvalid)
                entry.Handle.Dispose();
        }

        _logger.LogInformation("Removed global low-level hook {HookId}", hookId);
    }

    public void RegisterWinEvent(uint eventMin, uint eventMax)
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);

        WINEVENTPROC proc = OnWinEventCallback;

        var handle = PInvoke.SetWinEventHook(
            eventMin, 
            eventMax,
            HMODULE.Null,
            proc,
            0,
            0,
            WINEVENT_OUTOFCONTEXT);

        if (handle == IntPtr.Zero)
            throw new InvalidOperationException(
                $"SetWinEventHook(0x{eventMin:X4}–0x{eventMax:X4}) failed: " +
                $"0x{Marshal.GetLastWin32Error():X8}");

        _winEvents.Add(new WinEventEntry(proc, handle));
    }

    /// <summary>
    /// Runs for every input event on the machine, on the installing thread, with the whole
    /// desktop's input blocked behind it. Everything here is on that budget.
    /// </summary>
    /// <remarks>
    /// It used to run a LINQ scan over the hook list and allocate a
    /// <see cref="WindowsHookEventArgs"/> per event before invoking subscribers synchronously -
    /// on mouse move, that is hundreds of allocations a second in the one place where latency
    /// is charged to every application on the system. <see cref="InterestedInWParam"/> now
    /// rejects the overwhelmingly common case (mouse movement, which no subscriber wants)
    /// before anything is allocated at all.
    /// </remarks>
    private LRESULT LowLevelCallback(
        WINDOWS_HOOK_ID hookId, int nCode, WPARAM wParam, LPARAM lParam)
    {
        // Chain first and unconditionally: the rest of the system is waiting on this.
        // CallNextHookEx accepts a default handle, so there is no need to look ours up.
        var next = PInvoke.CallNextHookEx(default, nCode, wParam, lParam);

        if (nCode >= 0
            && Volatile.Read(ref _disposed) == 0
            && InterestedInWParam(hookId, wParam.Value))
        {
            HookFired?.Invoke(this, new WindowsHookEventArgs
            {
                HookId = hookId,
                NCode = nCode,
                WParam = wParam.Value,
                LParam = lParam.Value,
            });
        }

        return next;
    }

    /// <summary>
    /// Cheap pre-filter so the hot path allocates nothing for events nobody consumes. Mouse
    /// movement (WM_MOUSEMOVE) is by far the highest-volume event and has no subscriber -
    /// cursor position is polled separately (see WindowsMouseTracker), not taken from here.
    /// </summary>
    private static bool InterestedInWParam(WINDOWS_HOOK_ID hookId, nuint wParam) => hookId switch
    {
        // Mouse movement is the highest-volume event on the system and has no subscriber -
        // cursor position is polled separately (WindowsMouseTracker), not taken from here.
        WINDOWS_HOOK_ID.WH_MOUSE_LL => wParam != WM_MOUSEMOVE,
        // Only key-down matters for hotkeys (GlobalHotkeyService), so half the keyboard
        // traffic can be dropped before allocating anything.
        WINDOWS_HOOK_ID.WH_KEYBOARD_LL => wParam is WM_KEYDOWN or WM_SYSKEYDOWN,
        _ => true,
    };

    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_SYSKEYDOWN = 0x0104;

    private void OnWinEventCallback(
        HWINEVENTHOOK hWinEventHook,
        uint @event,
        HWND hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return;

        WinEventFired?.Invoke(this, new WinEventArgs
        {
            EventType = @event,
            WindowHandle = hwnd,
            ObjectId = idObject,
            ChildId = idChild,
            EventThread = idEventThread,
            EventTimeMs = dwmsEventTime,
        });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        foreach (var entry in _llHooks)
        {
            if (!entry.Handle.IsInvalid)
                entry.Handle.Dispose(); 
        }
        _llHooks.Clear();

        foreach (var entry in _winEvents)
        {
            if (entry.Handle != IntPtr.Zero)
                PInvoke.UnhookWinEvent(entry.Handle);
        }
        _winEvents.Clear();
    }
}