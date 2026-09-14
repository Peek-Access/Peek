namespace Peek.Core.Settings;

public sealed class ScreenshotSettings
{
    /// <summary>Whether the desktop (as opposed to a single window) may ever be captured - a broader-reaching privacy switch than window capture.</summary>
    public bool AllowDesktopCapture { get; set; } = true;
}
