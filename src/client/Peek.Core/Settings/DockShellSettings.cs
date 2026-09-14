using Peek.Core.Abstractions;

namespace Peek.Core.Settings;

/// <summary>
/// Which shell topology Peek starts in - see DockShellSettings for the dock edge/width
/// knobs that only matter when this is Docked.
/// </summary>
public enum ShellMode
{
    /// <summary>Today's shell: a normal, freely-moved MainWindow with a nav sidebar and one tab visible at a time.</summary>
    NormalWindow,

    /// <summary>
    /// A permanently docked strip (see IEdgeDockingService) reserving its own screen edge,
    /// hosting the same four monitor views cycled via a keyboard shortcut instead of a nav
    /// sidebar - the "reading-impaired user's dedicated control region" product direction.
    /// </summary>
    Docked,
}

/// <summary>
/// Startup shell topology and, when Docked, how the dock strip positions itself. Separate
/// from AutomationSettings because these are window-ergonomics knobs, not automation
/// behavior. Docking exists so selecting a node can bring its element's window to the
/// foreground without shoving the dock strip itself out of view - see IEdgeDockingService
/// for the mechanism.
/// </summary>
public sealed class DockShellSettings
{
    /// <summary>Decided once at startup (see App.axaml.cs) - switching requires a restart, not a live re-parenting of the shell's views.</summary>
    public ShellMode Mode { get; set; } = ShellMode.NormalWindow;

    public DockEdge DockEdge { get; set; } = DockEdge.Right;

    /// <summary>Width, in DIPs, of the reserved dock strip.</summary>
    public double DockWidth { get; set; } = 420;

    /// <summary>Whether cycling monitors (PageUp/PageDown or the dock header's prev/next buttons) plays a short audible tick.</summary>
    public bool PageSwitchSoundEnabled { get; set; } = true;
}
