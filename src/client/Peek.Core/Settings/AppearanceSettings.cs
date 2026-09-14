namespace Peek.Core.Settings;

public sealed class AppearanceSettings
{
    /// <summary>Only one theme exists today (Pipboy.Avalonia) - this field is the extension point for when that changes.</summary>
    public string Theme { get; set; } = "Pipboy";

    /// <summary>Hex color for the highlight overlay border - matches the observed default green (#39FF14).</summary>
    public string HighlightBorderColor { get; set; } = "#39FF14";

    /// <summary>Pipboy.Avalonia's primary accent color, picked in Settings - restored on
    /// startup so a chosen color survives a restart instead of resetting to the theme's
    /// built-in default every time.</summary>
    public string ThemeColor { get; set; } = "#39FF14";
}
