using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Peek.Core.Services;

public enum WindowLifecycleChangeKind
{
    Opened,
    Closed,
}

public sealed class WindowLifecycleChangedArgs : EventArgs
{
    public required WindowLifecycleChangeKind Kind { get; init; }
    public IntPtr WindowHandle { get; init; }
    public string WindowTitle { get; init; } = string.Empty;
    public uint ProcessId { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Raises <see cref="WindowLifecycleChanged"/> when a real, top-level, user-facing
/// window is created or destroyed - the "window open/close" half of window-change
/// announcements (<see cref="WindowTracker"/> is the "focus changed" half).
/// </summary>
/// <remarks>
/// EVENT_OBJECT_CREATE/DESTROY fire for far more than top-level windows - every
/// child control, tooltip, and helper window an app creates raises the same
/// events, so this filters to idObject==OBJID_WINDOW/idChild==CHILDID_SELF (the
/// window itself, not a child object within it) plus "not a child window,
/// not a tool window" (GetAncestor(hwnd, GA_PARENT) is the desktop, and
/// WS_EX_TOOLWINDOW isn't set - the same non-visible-helper-window heuristic
/// WindowEnumerator uses for IsToolWindow).
///
/// EVENT_OBJECT_DESTROY fires while the window handle is on the verge of being
/// destroyed, so re-querying its title/owner there is unreliable (it may already
/// return empty/stale data, or the handle may already be partially torn down).
/// Instead, only a handle this tracker itself classified as "opened" is ever
/// reported as "closed", using the title/pid captured at open time - this also
/// naturally suppresses close events for every window this tracker correctly
/// ignored as non-top-level/tool/child on open.
/// </remarks>
[SupportedOSPlatform("windows7.0")]
public sealed class WindowLifecycleTracker : IDisposable
{
    private const uint EVENT_OBJECT_CREATE = 0x8000;
    private const uint EVENT_OBJECT_DESTROY = 0x8001;
    private const int OBJID_WINDOW = 0;
    private const int CHILDID_SELF = 0;

    private readonly WindowsHookService _hookService;
    private readonly Dictionary<IntPtr, (string Title, uint ProcessId)> _trackedWindows = [];
    private readonly Lock _trackedWindowsLock = new();
    private int _disposed;

    public event EventHandler<WindowLifecycleChangedArgs>? WindowLifecycleChanged;

    public WindowLifecycleTracker(WindowsHookService hookService)
    {
        _hookService = hookService;
        _hookService.RegisterWinEvent(EVENT_OBJECT_CREATE, EVENT_OBJECT_DESTROY);
        _hookService.WinEventFired += OnWinEvent;
    }

    private void OnWinEvent(object? sender, WinEventArgs e)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return;

        if (e.WindowHandle == IntPtr.Zero || e.ObjectId != OBJID_WINDOW || e.ChildId != CHILDID_SELF)
            return;

        if (e.EventType == EVENT_OBJECT_CREATE)
            HandleCreated((HWND)e.WindowHandle);
        else if (e.EventType == EVENT_OBJECT_DESTROY)
            HandleDestroyed(e.WindowHandle);
    }

    private void HandleCreated(HWND hwnd)
    {
        if (!IsTopLevelUserWindow(hwnd))
            return;

        Span<char> titleBuf = stackalloc char[512];
        PInvoke.GetWindowText(hwnd, titleBuf);
        PInvoke.GetWindowThreadProcessId(hwnd, out uint pid);
        var title = titleBuf.ToString();

        lock (_trackedWindowsLock)
        {
            _trackedWindows[hwnd] = (title, pid);
        }

        WindowLifecycleChanged?.Invoke(this, new WindowLifecycleChangedArgs
        {
            Kind = WindowLifecycleChangeKind.Opened,
            WindowHandle = hwnd,
            WindowTitle = title,
            ProcessId = pid,
            Timestamp = DateTime.Now,
        });
    }

    private void HandleDestroyed(IntPtr hwnd)
    {
        (string Title, uint ProcessId) info;
        lock (_trackedWindowsLock)
        {
            if (!_trackedWindows.Remove(hwnd, out info))
                return;
        }

        WindowLifecycleChanged?.Invoke(this, new WindowLifecycleChangedArgs
        {
            Kind = WindowLifecycleChangeKind.Closed,
            WindowHandle = hwnd,
            WindowTitle = info.Title,
            ProcessId = info.ProcessId,
            Timestamp = DateTime.Now,
        });
    }

    private static bool IsTopLevelUserWindow(HWND hwnd)
    {
        var parent = PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_PARENT);
        if (parent != PInvoke.GetDesktopWindow())
            return false;

        var exStyle = (uint)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        if ((exStyle & (uint)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) != 0)
            return false;

        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _hookService.WinEventFired -= OnWinEvent;

        lock (_trackedWindowsLock)
        {
            _trackedWindows.Clear();
        }
    }
}
