namespace Peek.Core.Settings;

public sealed class LocalizationSettings
{
    /// <summary>BCP-47 culture name for the UI (e.g. "en-US", "zh-CN", "de-DE") - applied via ILinguaManager.UpdateCulture at startup.</summary>
    public string UiLanguage { get; set; } = "en-US";

    /// <summary>
    /// BCP-47 culture name for the primary/fallback spoken language - independent of
    /// UiLanguage (a screen's UI language and the language its content is read in aren't
    /// necessarily the same). Null means "follow UiLanguage". See SpeechLanguageDetector:
    /// Chinese text is always detected and read in Mandarin regardless of this setting;
    /// this is what a segment falls back to when it can't be determined (e.g. plain Latin
    /// script, which English and German can't reliably be told apart from by Unicode alone).
    /// </summary>
    public string? TtsLanguage { get; set; }

    public string? OcrLanguage { get; set; }
}
