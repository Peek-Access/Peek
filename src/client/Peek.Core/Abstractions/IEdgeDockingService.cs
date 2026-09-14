namespace Peek.Core.Abstractions;

/// <summary>Which screen edge, if any, a window is docked/reserved against - see <see cref="IEdgeDockingService"/>.</summary>
public enum DockEdge
{
    None,
    Left,
    Right,
}

/// <summary>
/// Reserves a strip of screen work area for a window and keeps it topmost there, the way
/// the taskbar or a sidebar gadget does. This is what lets the Element Inspector claim its
/// own permanent screen region: without it, bringing a selected element's window to the
/// foreground (see ElementInspectorViewModel.OnNodeSelectedAsync) pushes the Inspector's
/// own window behind it, since both windows compete for the same foreground z-order.
/// Docking sidesteps that paradox entirely - the Inspector isn't fighting for foreground,
/// it just always renders on top within its own reserved strip, like the taskbar does.
/// </summary>
/// <remarks>
/// Only a Windows implementation exists today (the Win32 AppBar API via
/// WindowsEdgeDockingService) - this interface is the seam a future macOS/Linux
/// implementation would slot into (see the OS branch in App.axaml.cs that registers it),
/// not a promise either is implemented yet.
/// </remarks>
public interface IEdgeDockingService : IDisposable
{
    /// <summary>False on a platform with no docking implementation - callers should treat Apply as a silent no-op rather than checking this first.</summary>
    bool IsSupported { get; }

    bool IsDocked { get; }

    /// <summary>
    /// Reserves <paramref name="widthDip"/> DIPs of the given edge's monitor and positions/keeps
    /// <paramref name="windowHandle"/> topmost there. Safe to call repeatedly (e.g. every time
    /// settings change) - re-applies in place rather than requiring a Remove first.
    /// </summary>
    void Apply(nint windowHandle, DockEdge edge, double widthDip);

    /// <summary>Releases the reserved work area and stops keeping the window docked. Safe to call when nothing is docked.</summary>
    void Remove();
}
