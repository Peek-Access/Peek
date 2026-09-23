using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using Peek.Core.Models;
using ReactiveUI.Primitives.Disposables;
using ReactiveUI.Primitives.Signals;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Peek.Core.Services;

[SupportedOSPlatform("windows7.0")]
public sealed class WindowEnumerator
{
    private readonly ILogger<WindowEnumerator> _logger;

    // Concurrent, not a plain Dictionary: EnumerateVisibleRoots (which FindMainWindowForProcess
    // calls synchronously) and EnumerateAll/EnumerateObservable's own Task.Run can now genuinely
    // run on different threads at once against this same singleton - callers on this class have
    // never been serialized, only kept off the UI thread individually.
    private readonly ConcurrentDictionary<uint, string> _processNameCache = new();
    private const int MaxTextLength = 512;

    public WindowEnumerator(ILogger<WindowEnumerator> logger)
        => _logger = logger;

    /// <summary>
    /// Restores (if minimized) and brings a window to the foreground - used by the
    /// Element Inspector so selecting a node whose window is minimized or sitting behind
    /// other windows actually shows the user what they picked, not just a highlight box
    /// drawn over whatever happens to be on top. Best-effort: SetForegroundWindow can be
    /// silently refused by Windows' foreground-lock heuristics in edge cases: not treated
    /// as an error, since the highlight overlay still gets drawn (see IHighlightService)
    /// which is normally enough to locate the element regardless.
    /// </summary>
    /// <returns>True if the window had to be restored and/or foregrounded - callers can use this to know whether to wait out a restore animation before trusting the window's bounds.</returns>
    public bool EnsureWindowForeground(nint hwnd)
    {
        var hWnd = (HWND)hwnd;
        var changed = false;

        if (PInvoke.IsIconic(hWnd))
        {
            PInvoke.ShowWindow(hWnd, SHOW_WINDOW_CMD.SW_RESTORE);
            changed = true;
        }

        if (PInvoke.GetForegroundWindow() != hWnd)
        {
            PInvoke.SetForegroundWindow(hWnd);
            changed = true;
        }

        return changed;
    }

    public IReadOnlyList<WindowNode> EnumerateAll(bool includeChildren = true)
    {
        _processNameCache.Clear();
        var results = new List<WindowNode>(512);

        PInvoke.EnumWindows((hwnd, _) =>
        {
            try
            {
                results.Add(SnapshotWindow(hwnd, parentHwnd: 0, depth: 0));
                if (includeChildren)
                    EnumerateChildren(hwnd, results, depth: 1);
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Failed to snapshot HWND 0x{Hwnd:X}", hwnd);
            }
            return true;
        }, 0);

        return results;
    }

    /// <summary>
    /// Smallest window, in either dimension, that a person could plausibly aim at. Anything
    /// under this is a helper window that happens to have a rectangle.
    /// </summary>
    /// <remarks>
    /// 32 rather than something rounder because the taskbar is 48 pixels tall and is a real,
    /// visible thing worth being able to inspect - a threshold chosen without looking would
    /// very likely have removed it. The window this does remove, measured on a real desktop,
    /// is a 12x37 unnamed "CursorVisualClass" helper.
    /// </remarks>
    public const int MinimumPointableSize = 32;

    /// <summary>
    /// Whether a visible top-level window is one the user could actually point at, given its
    /// size, minimised state and DWM cloak state.
    /// </summary>
    /// <remarks>
    /// Separated from the enumeration so the rule can be tested without a desktop, and written
    /// from a measurement of what the noise actually was rather than from a guess:
    /// <list type="bullet">
    /// <item><b>Cloaked windows</b> are the important case and the one a size threshold can
    /// never catch. A suspended UWP app stays "visible" to <c>IsWindowVisible</c> while DWM
    /// hides it, and two of them - Settings and Windows Input Experience - were sitting in the
    /// list at 1920x1032 and 1920x1080. They looked like the most significant windows on the
    /// desktop and were not on screen at all.</item>
    /// <item><b>Tiny windows</b> are the case the size threshold is for, and it is a small
    /// part of the problem: exactly one on the measured desktop.</item>
    /// <item><b>Minimised windows are exempt from the size check.</b> Their rectangle is a
    /// meaningless 160x28 stub, but they are real windows with real titles, and selecting one
    /// in the Inspector is how a user restores it (see EnsureWindowForeground). Filtering them
    /// out by size would have quietly removed a feature.</item>
    /// </list>
    /// </remarks>
    public static bool ShouldList(bool isMinimized, int width, int height, bool isCloaked)
    {
        if (isCloaked) return false;
        if (isMinimized) return true;

        return width >= MinimumPointableSize && height >= MinimumPointableSize;
    }

    /// <summary>
    /// True when DWM is hiding this window despite <c>IsWindowVisible</c> reporting otherwise -
    /// a suspended UWP app, or a shell helper. Any non-zero cloak value counts; the distinction
    /// between "cloaked by the app" and "cloaked by the shell" does not matter here.
    /// </summary>
    private static unsafe bool IsCloaked(nint hwnd)
    {
        int cloaked = 0;
        var hr = PInvoke.DwmGetWindowAttribute(
            (HWND)hwnd,
            DWMWINDOWATTRIBUTE.DWMWA_CLOAKED,
            &cloaked,
            sizeof(int));

        // A failure means "no opinion" - on anything older or stranger than expected, keeping
        // the window is the safer answer than hiding one the user can see.
        return hr.Succeeded && cloaked != 0;
    }

    /// <summary>
    /// Runs one throwaway enumeration on a background thread to pay this path's first-call cost
    /// before a user is waiting on it.
    /// </summary>
    /// <remarks>
    /// Measured on a packaged build: the Inspector's first window enumeration takes ~534ms and
    /// every subsequent one 32-70ms. The difference is almost entirely JIT and first-touch of
    /// the P/Invoke stubs, not work - which is why the *first* switch to the window monitor
    /// felt slow and later ones did not. Doing it here, deliberately late and off the UI
    /// thread, moves that cost somewhere nobody is watching.
    /// <para>
    /// Deliberately delayed rather than run at startup: startup is already the most contended
    /// moment in this app's life, and a global mouse hook installed there recently made the
    /// whole desktop stutter. This is background CPU only - no hooks, no UI thread - but it
    /// still has no business competing with the window actually appearing.
    /// </para>
    /// </remarks>
    public async Task WarmUpAsync(TimeSpan delay, CancellationToken ct = default)
    {
        try
        {
            await Task.Delay(delay, ct).ConfigureAwait(false);
            await Task.Run(() => EnumerateVisibleRoots(), ct).ConfigureAwait(false);
            _logger.LogDebug("Window enumeration warmed up");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Window enumeration warm-up failed");
        }
    }

    /// <summary>
    /// Top-level windows a person could actually point at: visible, occupying real screen
    /// space, and optionally excluding one process (Peek's own).
    /// </summary>
    /// <remarks>
    /// Exists because the Inspector was getting this list by snapshotting every top-level
    /// window on the desktop and then filtering, and the filtering threw away most of the work:
    /// a full <see cref="SnapshotWindow"/> is ten P/Invokes plus a process-name resolution, and
    /// roughly 490 top-level windows exist on a normal desktop to produce the ~37 rows the
    /// Inspector shows. The three cheap rejects below run first, so the expensive part is paid
    /// only for windows that will actually be displayed.
    /// </remarks>
    public IReadOnlyList<WindowNode> EnumerateVisibleRoots(uint? excludeProcessId = null)
    {
        _processNameCache.Clear();
        var results = new List<WindowNode>(64);

        PInvoke.EnumWindows((hwnd, _) =>
        {
            try
            {
                if (!PInvoke.IsWindowVisible((HWND)hwnd))
                    return true;

                PInvoke.GetWindowRect((HWND)hwnd, out RECT rect);
                if (rect.Width <= 0 || rect.Height <= 0)
                    return true;

                if (excludeProcessId is { } skip)
                {
                    PInvoke.GetWindowThreadProcessId((HWND)hwnd, out uint pid);
                    if (pid == skip) return true;
                }

                // Cloak state last of the cheap checks: it is a DWM call, and most windows
                // are rejected or accepted before reaching it.
                if (!ShouldList(
                        isMinimized: PInvoke.IsIconic((HWND)hwnd),
                        width: rect.Width,
                        height: rect.Height,
                        isCloaked: IsCloaked(hwnd)))
                {
                    return true;
                }

                results.Add(SnapshotWindow(hwnd, parentHwnd: 0, depth: 0));
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Failed to snapshot HWND 0x{Hwnd:X}", hwnd);
            }

            return true;
        }, 0);

        return results;
    }

    /// <summary>
    /// The first visible top-level window owned by <paramref name="processId"/>, if any - lets
    /// Announcement History's replay bring a Process Monitor selection's window forward the
    /// same way an element-sourced entry does. A process can own several top-level windows (or
    /// none, e.g. a background service); this is a best-effort "the" window, not an exhaustive
    /// list - good enough for "switch to whatever this process is showing".
    /// </summary>
    /// <remarks>
    /// Must run off the UI thread, same as <see cref="EnumerateVisibleRoots"/> itself (see its
    /// callers' own remarks) - this walks every top-level window and is not cheap.
    /// </remarks>
    public WindowNode? FindMainWindowForProcess(uint processId) =>
        EnumerateVisibleRoots().FirstOrDefault(w => w.ProcessId == processId);

    public IObservable<WindowNode> EnumerateObservable(bool includeChildren = true)
    {
        return Signal.Create<WindowNode>(observer =>
        {
            _processNameCache.Clear();

            try
            {
                PInvoke.EnumWindows((hwnd, _) =>
                {
                    try
                    {
                        var node = SnapshotWindow(hwnd, parentHwnd: 0, depth: 0);
                        observer.OnNext(node);

                        if (includeChildren)
                        {
                            EnumerateChildrenObservable(hwnd, observer, depth: 1);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogTrace(ex, "Failed to snapshot HWND 0x{Hwnd:X}", hwnd);
                    }

                    return true; 
                }, 0);

                observer.OnCompleted();
            }
            catch (Exception ex)
            {
                observer.OnError(ex);
            }
            return new ActionDisposable(() => { });
        });
    }
    private void EnumerateChildrenObservable(HWND parentHwnd, 
        IObserver<WindowNode> observer,
        int depth)
    {
        PInvoke.EnumChildWindows(parentHwnd, (hwnd, _) =>
        {
            try
            {
                var node = SnapshotWindow(hwnd, parentHwnd, depth);
                observer.OnNext(node);

                EnumerateChildrenObservable(hwnd, observer, depth + 1);
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Failed to snapshot child HWND 0x{Hwnd:X}", hwnd);
            }

            return true;
        }, 0);
    }
    public WindowNode? SnapshotSingle(nint hwnd)
    {
        if (!PInvoke.IsWindow(new HWND(hwnd))) return null;
        try
        {
            var parent = PInvoke.GetAncestor((HWND)hwnd, GET_ANCESTOR_FLAGS.GA_PARENT);
            return SnapshotWindow(hwnd, parent, depth: 0);
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to snapshot HWND 0x{Hwnd:X}", hwnd);
            return null;
        }
    }

    private void EnumerateChildren(nint parentHwnd, List<WindowNode> results, int depth)
    {
        PInvoke.EnumChildWindows(new HWND(parentHwnd), (hwnd, _) =>
        {
            try { results.Add(SnapshotWindow(hwnd, parentHwnd, depth)); }
            catch (Exception ex) { _logger.LogTrace(ex, "Child HWND 0x{Hwnd:X}", hwnd); }
            return true;
        }, 0);
    }

    private WindowNode SnapshotWindow(nint hwnd, nint parentHwnd, int depth)
    {
        Span<char> titleBuf = stackalloc char[512];
        PInvoke.GetWindowText((HWND)hwnd, titleBuf);

        Span<char> classBuf = stackalloc char[256];
        PInvoke.GetClassName((HWND)hwnd, classBuf);

        PInvoke.GetWindowThreadProcessId((HWND)hwnd, out uint processId);

        var style = (uint)PInvoke.GetWindowLong((HWND)hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        var exStyle = (uint)PInvoke.GetWindowLong((HWND)hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

        var ownerHwnd = PInvoke.GetWindow((HWND)hwnd, GET_WINDOW_CMD.GW_OWNER);

        PInvoke.GetWindowRect((HWND)hwnd, out RECT r);

        return new WindowNode
        {
            Hwnd = hwnd,
            ParentHwnd = parentHwnd,
            OwnerHwnd = ownerHwnd,
            Title = titleBuf.ToString(),
            ClassName = classBuf.ToString(),
            ProcessId = processId,
            ProcessName = GetProcessName(processId),
            Rect = new WindowRect(r.left, r.top, r.Width, r.Height),
            Style = style,
            ExStyle = exStyle,

            IsVisible = PInvoke.IsWindowVisible((HWND)hwnd),
            IsEnabled = (style & (uint)WINDOW_STYLE.WS_DISABLED) == 0,
            IsMinimized = PInvoke.IsIconic((HWND)hwnd),
            IsMaximized = PInvoke.IsZoomed((HWND)hwnd),

            IsTopmost = (exStyle & (uint)WINDOW_EX_STYLE.WS_EX_TOPMOST) != 0,
            IsToolWindow = (exStyle & (uint)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) != 0,
            IsRoot  = parentHwnd == 0,

            Depth = depth
        };
    }

    /// <summary>Resolves a process id to its owning executable's name (no extension), cached per enumeration/lookup.</summary>
    public unsafe string GetProcessName(uint pid)
    {
        if (_processNameCache.TryGetValue(pid, out var cached))
            return cached;

        HANDLE handle = PInvoke.OpenProcess(
            PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            pid);

        if (handle == HANDLE.Null)
        {
            _processNameCache[pid] = string.Empty;
            return string.Empty;
        }

        // Wrap the raw HANDLE in a SafeFileHandle for PInvoke.CloseHandle
        using var safeHandle = new SafeProcessHandle((HWND)handle.Value, ownsHandle: true);

        // No 'unsafe' block needed since we are using Span<char>
        Span<char> buffer = stackalloc char[1024];
        uint size = (uint)buffer.Length;

        // Argument 3 now correctly receives the Span<char> buffer
        if (PInvoke.QueryFullProcessImageName(safeHandle, 0, buffer, ref size))
        {
            // size is modified by the API to represent the characters written
            var fullPath = buffer[..(int)size].ToString();
            var name = Path.GetFileNameWithoutExtension(fullPath);

            _processNameCache[pid] = name;
            return name;
        }

        // Cache the failure too. Without this, every pid that can't be queried - protected and
        // system processes, which a desktop has plenty of - paid a fresh OpenProcess and
        // QueryFullProcessImageName for *every window it owns*, on every enumeration.
        _processNameCache[pid] = string.Empty;
        return string.Empty;
    }
}
