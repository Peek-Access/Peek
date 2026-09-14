namespace Peek.Core.Settings;

/// <summary>Endpoint/credentials/model for one LLM provider - see AiSettings.Providers.</summary>
public sealed class LlmProviderCredentials
{
    /// <summary>Base URL. Empty means "use the provider's well-known default" (see LlmProviderFactory on the worker) - required for "custom".</summary>
    public string Endpoint { get; set; } = "";

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "";
}

/// <summary>
/// AI/LLM feature configuration (§15). The worker is stateless with respect to these
/// settings (§17) - every request resolves the active provider's LlmProviderCredentials
/// here into a LlmProviderConfig sent with the RPC call, so switching providers takes
/// effect immediately, with no worker restart.
/// </summary>
public sealed class AiSettings
{
    /// <summary>Master switch for LLM features.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Which entry of <see cref="Providers"/> is active: "ollama", "openai", "anthropic", "gemini", "openrouter", or "custom".</summary>
    public string Provider { get; set; } = "ollama";

    public float Temperature { get; set; } = 0.2f;

    // Kept modest on purpose: responses are spoken aloud (§14 "prefer concise
    // speech-friendly answers"), and TTS synthesis time scales with text length -
    // a long response is worse on both accessibility and latency grounds.
    public int MaxTokens { get; set; } = 120;

    /// <summary>Higher token budget for the longer-form screen/window analysis narration (see ScreenAnalysisService) - a single-sentence element description doesn't need this much room, but "what is this window and what can I do with it" genuinely does.</summary>
    public int ScreenAnalysisMaxTokens { get; set; } = 500;

    /// <summary>Appended after the fixed accessibility base prompt (§14) - never replaces it. See AccessibilityPromptBuilder.</summary>
    public string? SystemPromptCustomization { get; set; }

    public Dictionary<string, LlmProviderCredentials> Providers { get; set; } = new()
    {
        ["ollama"] = new() { Endpoint = "http://localhost:11434", Model = "llama3.2:latest" },
        ["openai"] = new() { Endpoint = "https://api.openai.com/v1", Model = "gpt-4o-mini" },
        ["anthropic"] = new() { Endpoint = "https://api.anthropic.com", Model = "claude-3-5-haiku-latest" },
        ["gemini"] = new() { Endpoint = "https://generativelanguage.googleapis.com", Model = "gemini-2.0-flash" },
        ["openrouter"] = new() { Endpoint = "https://openrouter.ai/api/v1", Model = "openai/gpt-4o-mini" },
        ["custom"] = new() { Endpoint = "", Model = "" },
    };

    public void Validate()
    {
        if (Temperature < 0f || float.IsNaN(Temperature)) Temperature = 0.2f;
        if (MaxTokens <= 0) MaxTokens = 120;
        if (ScreenAnalysisMaxTokens <= 0) ScreenAnalysisMaxTokens = 500;
        if (string.IsNullOrWhiteSpace(Provider)) Provider = "ollama";
    }
}
