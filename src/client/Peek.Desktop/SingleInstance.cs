using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Peek.Desktop;

/// <summary>
/// Finds and activates an already-running Peek.Desktop's window when a second instance is
/// launched (see Program.Main's single-instance mutex check). Matches by process name
/// rather than window title/class so it works regardless of localized/customized window
/// titles, and regardless of whether the existing window is minimized or hidden to the
/// tray (FindWindow-by-title would miss a hidden window; enumerating by owning process does
/// not).
/// </summary>
internal static class SingleInstance
{
    private const uint GwOwner = 4;
    private const int SwRestore = 9;
    private const int SwShow = 5;

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    public static void ActivateExistingInstance()
    {
        var currentProcessId = (uint)Environment.ProcessId;
        var otherProcessIds = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName("Peek.Desktop"))
        {
            if ((uint)process.Id != currentProcessId)
                otherProcessIds.Add((uint)process.Id);
            process.Dispose();
        }

        if (otherProcessIds.Count == 0) return;

        var found = nint.Zero;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            if (!otherProcessIds.Contains(pid)) return true;
            if (GetWindow(hWnd, GwOwner) != nint.Zero) return true; // owned/tool window - keep looking

            found = hWnd;
            return false; // stop enumeration
        }, nint.Zero);

        if (found == nint.Zero) return;

        // SW_RESTORE alone un-minimizes; a window merely hidden to the tray (not minimized)
        // needs SW_SHOW too - harmless either way, so both are sent.
        ShowWindow(found, SwShow);
        ShowWindow(found, SwRestore);
        SetForegroundWindow(found);
    }
}
