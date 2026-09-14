namespace Peek.Worker.Llm;

/// <summary>
/// Configuration for a remote/OpenAI-compatible provider. Never enabled by default -
/// Program.cs only registers <see cref="OpenAiCompatibleProvider"/> when this is
/// explicitly configured with an endpoint (§15/§16: remote providers are explicit
/// opt-in, never silently enabled).
/// </summary>
public sealed class OpenAiCompatibleOptions
{
    public string? Endpoint { get; set; }

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gpt-4o-mini";

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
