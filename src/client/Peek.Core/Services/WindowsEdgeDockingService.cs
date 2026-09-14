using Microsoft.Extensions.Logging;
using Peek.Core.Abstractions;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Peek.Core.Services;

/// <summary>
/// Reserves screen work area for a window via the Win32 "AppBar" API (SHAppBarMessage) -
/// the same mechanism the taskbar and tools like Rainmeter's docked sidebars use, so
/// maximized windows shrink to avoid the strip and the docked window itself can stay
/// topmost within it. See IEdgeDockingService for why the Element Inspector needs this.
/// </summary>
/// <remarks>
/// SHAppBarMessage/APPBARDATA are declared by hand here (raw DllImport) rather than via
/// CsWin32/NativeMethods.txt like everything else in this file: the pinned CsWin32 metadata
/// build (71.0.14-preview) doesn't generate shell32's AppBar surface at all - confirmed by
/// temporarily enabling EmitCompilerGeneratedFiles and finding no SHELL32.dll.g.cs. Every
/// other type here (HWND, RECT, MONITORINFO, SUBCLASSPROC, ...) is the normal CsWin32-
/// generated one.
/// </remarks>
[SupportedOSPlatform("windows7.0")]
public sealed class WindowsEdgeDockingService : IEdgeDockingService
{
    private const uint AbmNew = 0x00000000;
    private const uint AbmRemove = 0x00000001;
    private const uint AbmQueryPos = 0x00000002;
    private const uint AbmSetPos = 0x00000003;
    private const uint AbmWindowPosChanged = 0x00000009;
    private const uint AbnPosChanged = 0x00000001;

    // ABE_LEFT / ABE_RIGHT (AppBarData.uEdge).
    private const uint AbeLeft = 0;
    private const uint AbeRight = 2;

    // WM_WINDOWPOSCHANGED - not something we already pull a named constant in for.
    private const uint WmWindowPosChanged = 0x0047;

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public nint lParam;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern nuint SHAppBarMessage(uint dwMessage, ref AppBarData data);

    private readonly ILogger<WindowsEdgeDockingService> _logger;
    private readonly SUBCLASSPROC _subclassProcedure;
    private HWND _windowHandle;
    private uint _callbackMessage;
    private DockEdge _edge;
    private double _widthDip;
    private bool _applying;

    public WindowsEdgeDockingService(ILogger<WindowsEdgeDockingService> logger)
    {
        _logger = logger;
        _subclassProcedure = WindowProcedure;
    }

    public bool IsSupported => true;
    public bool IsDocked { get; private set; }

    public void Apply(nint windowHandle, DockEdge edge, double widthDip)
    {
        if (_applying) return;

        if (windowHandle == 0 || edge == DockEdge.None)
        {
            Remove();
            return;
        }

        var hwnd = new HWND(windowHandle);

        if (IsDocked && _windowHandle != hwnd)
            Remove();

        _windowHandle = hwnd;
        _edge = edge;
        _widthDip = widthDip;

        if (!IsDocked)
        {
            _callbackMessage = PInvoke.RegisterWindowMessage($"Peek.EdgeDocking.{Environment.ProcessId}");
            var registration = CreateData();
            SHAppBarMessage(AbmNew, ref registration);
            PInvoke.SetWindowSubclass(hwnd, _subclassProcedure, 1, 0);
            IsDocked = true;
        }

        _applying = true;
        try
        {
            ApplyPosition(hwnd, edge, widthDip);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply edge docking");
        }
        finally
        {
            _applying = false;
        }
    }

    private unsafe void ApplyPosition(HWND hwnd, DockEdge edge, double widthDip)
    {
        var monitor = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (monitor.IsNull || !PInvoke.GetMonitorInfo(monitor, &monitorInfo))
            return;

        var dpi = PInvoke.GetDpiForWindow(hwnd);
        var physicalWidth = Math.Max(1, (int)Math.Round(widthDip * (dpi == 0 ? 1 : dpi / 96d)));

        var data = CreateData();
        data.uEdge = edge == DockEdge.Left ? AbeLeft : AbeRight;
        data.rc = monitorInfo.rcMonitor;
        if (edge == DockEdge.Left)
            data.rc.right = data.rc.left + physicalWidth;
        else
            data.rc.left = data.rc.right - physicalWidth;

        SHAppBarMessage(AbmQueryPos, ref data);

        if (edge == DockEdge.Left)
            data.rc.right = data.rc.left + physicalWidth;
        else
            data.rc.left = data.rc.right - physicalWidth;

        SHAppBarMessage(AbmSetPos, ref data);

        PInvoke.SetWindowPos(
            hwnd,
            new HWND(new nint(-1)), // HWND_TOPMOST
            data.rc.left,
            data.rc.top,
            data.rc.right - data.rc.left,
            data.rc.bottom - data.rc.top,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
    }

    public void Remove()
    {
        if (!IsDocked) return;

        PInvoke.RemoveWindowSubclass(_windowHandle, _subclassProcedure, 1);
        var data = CreateData();
        SHAppBarMessage(AbmRemove, ref data);
        IsDocked = false;
        _windowHandle = HWND.Null;
        _callbackMessage = 0;
    }

    public void Dispose() => Remove();

    private unsafe AppBarData CreateData() => new()
    {
        cbSize = (uint)sizeof(AppBarData),
        hWnd = _windowHandle,
        uCallbackMessage = _callbackMessage,
    };

    private LRESULT WindowProcedure(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam, nuint uIdSubclass, nuint dwRefData)
    {
        if (message == _callbackMessage && (uint)wParam.Value == AbnPosChanged)
        {
            Apply(hwnd, _edge, _widthDip);
            return (LRESULT)0;
        }

        if (message == WmWindowPosChanged && IsDocked && !_applying)
        {
            var data = CreateData();
            SHAppBarMessage(AbmWindowPosChanged, ref data);
        }

        return PInvoke.DefSubclassProc(hwnd, message, wParam, lParam);
    }
}
