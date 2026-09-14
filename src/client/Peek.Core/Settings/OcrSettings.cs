using Peek.Core.Services.Ocr;

namespace Peek.Core.Settings;

public sealed class OcrSettings
{
    public OcrUserPreference Preference { get; set; } = OcrUserPreference.Automatic;

    /// <summary>The §3 "Additional text may be available..." wording - overrides OcrSuggestionMessages.Default when set.</summary>
    public string? SuggestionMessage { get; set; }

    /// <summary>
    /// Process names (as <see cref="Services.WindowEnumerator.GetProcessName"/> returns them -
    /// no ".exe") known to expose little/no useful UI Automation content - feeds
    /// OcrDecisionContext.IsKnownProblematicApplication, matched case-insensitively by
    /// <see cref="Ocr.OcrFallbackAnnouncer"/>. Seeded with WeChat's two Windows client names
    /// (Tencent ships "Weixin.exe" for the mainland-China build, "WeChat.exe" elsewhere) - both
    /// render their message list as a single custom-drawn surface with no per-message UIA
    /// content, so hovering/focusing it announces only the window itself without this.
    /// User-extensible for any other app with the same problem.
    /// </summary>
    public List<string> KnownProblematicApplications { get; set; } = ["Weixin", "WeChat"];
}
