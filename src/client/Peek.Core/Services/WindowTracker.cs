using Peek.Core.Models;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Peek.Core.Services;



public sealed class ForegroundWindowChangedArgs : EventArgs
{
    public IntPtr WindowHandle { get; init; }
    public string WindowTitle { get; init; } = string.Empty;
    public uint ProcessId { get; init; }
    public DateTime Timestamp { get; init; }
}

[SupportedOSPlatform("windows7.0")]
public sealed class WindowTracker : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;

    private readonly WindowsHookService _hookService;
    private readonly WindowEnumerator _windowEnumerator;
    private int _disposed;
    public event EventHandler<ForegroundWindowChangedArgs>? ForegroundWindowChanged;

    //private readonly SourceCache<WindowNode, nint> _windowNodeCache = new(node => node.Hwnd);

    public WindowTracker(WindowsHookService hookService, WindowEnumerator windowEnumerator)
    {
        _hookService = hookService;
        _windowEnumerator = windowEnumerator;
        _hookService.RegisterWinEvent(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND);
        _hookService.WinEventFired += OnWinEvent;
    }

    public IReadOnlyList<WindowNode> EnumerateAll()
    {
        return _windowEnumerator.EnumerateAll();
    }
    private void OnWinEvent(object? sender, WinEventArgs e)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return;

        // Filter: only foreground changes with a valid window handle
        if (e.EventType != EVENT_SYSTEM_FOREGROUND || e.WindowHandle == IntPtr.Zero)
            return;

        Span<char> titleBuf = stackalloc char[512];
        PInvoke.GetWindowText((HWND)e.WindowHandle, titleBuf);
        PInvoke.GetWindowThreadProcessId((HWND)e.WindowHandle, out uint pid);

        ForegroundWindowChanged?.Invoke(this, new ForegroundWindowChangedArgs
        {
            WindowHandle = e.WindowHandle,
            WindowTitle = titleBuf.ToString(),
            ProcessId = pid,
            Timestamp = DateTime.Now,
        });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _hookService.WinEventFired -= OnWinEvent;
    }
}