namespace Peek.Core.Settings;

/// <summary>Controls the Process Monitor dock/tab view - see ProcessMonitorViewModel.</summary>
public sealed class ProcessMonitorSettings
{
    /// <summary>Off by default: a full unfiltered process list is mostly service/session-0 noise a reading-impaired user has no use for browsing.</summary>
    public bool ShowSystemProcesses { get; set; } = false;

    /// <summary>False = announce "name, pid" on selection. True = also include memory usage.</summary>
    public bool AnnounceDetailedInfo { get; set; } = false;
}
