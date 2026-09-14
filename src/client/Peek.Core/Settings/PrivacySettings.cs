namespace Peek.Core.Settings;

/// <summary>
/// The explicit-consent gates from §16 ("must not silently transmit... Privacy
/// settings should clearly communicate what data leaves the machine"). These are
/// checked in addition to (not instead of) AiSettings' remote provider fields being
/// configured - both must be true before anything remote is ever used.
/// </summary>
public sealed class PrivacySettings
{
    /// <summary>Must be explicitly true before any AiSettings.Provider other than "ollama" is ever used - see LlmProviderConfigResolver, the one place this is checked.</summary>
    public bool AllowRemoteLlm { get; set; }
}
