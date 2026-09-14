namespace Peek.Core.Settings;

public sealed class AutomationSettings
{
    /// <summary>Master switch for UI Automation tracking - turning this off effectively makes OCR the only information source everywhere.</summary>
    public bool Enabled { get; set; } = true;
}
