using Peek.Core.Abstractions;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;
using System.Drawing;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Peek.Core.Services;

[SupportedOSPlatform("windows6.0")]
public sealed class WindowsMouseTracker : IMouseTracker, IDisposable
{
    private const uint WM_LBUTTONUP = 0x0202;

    private readonly BehaviorSignal<bool> _enabled = new(true);
    private readonly Signal<RxVoid> _selectedTextSubject = new();
    private readonly WindowsHookService _hookService;
    private int _disposed;

    public IObservable<Point> MousePositionStream { get; }
    public IObservable<RxVoid> SelectedStream { get; }

    public WindowsMouseTracker(
        WindowsHookService hookService,
        int intervalMs = 20)
    {
        _hookService = hookService;

        // The WH_MOUSE_LL hook is deliberately NOT installed here, even though this service is
        // constructed during startup: a global low-level mouse hook installed on the UI thread
        // at that moment would stutter the cursor desktop-wide, since Windows routes every
        // mouse event on the machine through such a hook and stalls input until it returns. It
        // is installed on Resume and removed on Pause instead: this hook exists only to notice
        // a click for text selection, which is meaningless while tracking is off anyway.
        _hookService.HookFired += OnHookFired;

        MousePositionStream =
            Signal.Interval(TimeSpan.FromMilliseconds(intervalMs))
                .Select(_ =>
                {
                    PInvoke.GetCursorPos(out var p);
                    return p;
                })
                .Where(pos => !IsOwnWindow(pos.X, pos.Y))
                .CombineLatest(_enabled, (pos, enabled) => (pos, enabled))
                .Where(x => x.enabled)
                .Select(x => x.pos)
                .DistinctUntilChanged()
                .Publish()
                .RefCount();

        SelectedStream = _selectedTextSubject
            .CombineLatest(_enabled, (unit, enabled) => (unit, enabled))
            .Where(x => x.enabled)
            .Select(x => x.unit)
            .Publish()
            .RefCount();
    }

    public void Pause()
    {
        _enabled.OnNext(false);
        _hookService.UnregisterLowLevelHook(WINDOWS_HOOK_ID.WH_MOUSE_LL);
    }

    public void Resume()
    {
        if (Volatile.Read(ref _disposed) == 1) return;

        _hookService.RegisterLowLevelHook(WINDOWS_HOOK_ID.WH_MOUSE_LL);
        _enabled.OnNext(true);
    }

    private void OnHookFired(object? sender, WindowsHookEventArgs e)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return;

        if (e.HookId != WINDOWS_HOOK_ID.WH_MOUSE_LL) return;

        if (e.WParam == WM_LBUTTONUP && _enabled.Value)
            _selectedTextSubject.OnNext(RxVoid.Default);
    }

    private static bool IsOwnWindow(int x, int y)
    {
        var hwnd = PInvoke.WindowFromPoint(new Point(x, y));
        PInvoke.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _hookService.HookFired -= OnHookFired;

        _selectedTextSubject.OnCompleted();
        _selectedTextSubject.Dispose();

        _enabled.OnCompleted();
        _enabled.Dispose();
    }
}