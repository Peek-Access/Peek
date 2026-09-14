namespace Peek.Worker.Llm;

/// <summary>Configuration for Anthropic's Messages API (Claude). Remote/opt-in only - see LlmProviderFactory, never enabled implicitly (§16).</summary>
public sealed class AnthropicOptions
{
    public string Endpoint { get; set; } = "https://api.anthropic.com";

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-3-5-haiku-latest";

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
