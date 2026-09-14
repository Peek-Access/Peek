namespace Peek.Worker.Llm;

/// <summary>Configuration for Google's Gemini API. Remote/opt-in only - see LlmProviderFactory, never enabled implicitly (§16).</summary>
public sealed class GeminiOptions
{
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com";

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gemini-2.0-flash";

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
