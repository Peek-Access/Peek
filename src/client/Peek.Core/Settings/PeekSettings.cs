namespace Peek.Core.Settings;

/// <summary>
/// The root, versioned settings aggregate (§17). Each sub-settings class is
/// independent so future UI/features can bind to just the piece they own; this
/// type only exists to give them one persisted, versioned home.
/// </summary>
public sealed class PeekSettings
{
    /// <summary>Bump when a breaking change to any sub-settings shape needs a migration - see <see cref="SettingsMigration"/>.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public AccessibilitySettings Accessibility { get; set; } = new();
    public SpeechSettings Speech { get; set; } = new();
    public OcrSettings Ocr { get; set; } = new();
    public AutomationSettings Automation { get; set; } = new();
    public ScreenshotSettings Screenshot { get; set; } = new();
    public AiSettings Ai { get; set; } = new();
    public LocalizationSettings Localization { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
    public KeyboardSettings Keyboard { get; set; } = new();
    public PrivacySettings Privacy { get; set; } = new();
    public AdvancedSettings Advanced { get; set; } = new();
    public WindowAnnouncementSettings WindowAnnouncement { get; set; } = new();
    public DockShellSettings DockShell { get; set; } = new();
    public ProcessMonitorSettings ProcessMonitor { get; set; } = new();
    public AnnouncementHistorySettings AnnouncementHistory { get; set; } = new();

    /// <summary>Clamps/corrects values that can't be trusted as-is (e.g. a hand-edited settings.json) rather than throwing - called after load and before save.</summary>
    public void Validate()
    {
        Accessibility.Validate();
        Speech.Validate();
        Ai.Validate();
        AnnouncementHistory.Validate();
    }
}
