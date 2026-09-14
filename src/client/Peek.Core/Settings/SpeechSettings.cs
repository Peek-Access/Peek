using Peek.Core.Services.Speech;

namespace Peek.Core.Settings;

public sealed class SpeechSettings
{
    public SpeechVerbosity Verbosity { get; set; } = SpeechVerbosity.Standard;

    /// <summary>
    /// Piper voice model key (e.g. "en_US-lessac-medium") per two-letter language code.
    /// SpeechLanguageDetector tags each run of announced text with one of these codes -
    /// "zh" whenever it recognizes Chinese characters, otherwise the user's primary
    /// Localization.TtsLanguage - so mixed-language text is spoken with the right voice per
    /// run instead of one voice for the whole utterance. Defaults cover all three
    /// UI-supported languages.
    /// </summary>
    public Dictionary<string, string> VoiceIdByLanguage { get; set; } = new()
    {
        ["en"] = "en_US-lessac-medium",
        ["de"] = "de_DE-thorsten-medium",
        ["zh"] = "zh_CN-huayan-medium",
    };

    /// <summary>Piper speaking-rate multiplier - lower is faster, higher is slower (matches PiperConfiguration.SpeakingRate).</summary>
    public float Rate { get; set; } = 1.0f;

    public void Validate()
    {
        if (Rate <= 0f || float.IsNaN(Rate) || float.IsInfinity(Rate)) Rate = 1.0f;
    }
}
