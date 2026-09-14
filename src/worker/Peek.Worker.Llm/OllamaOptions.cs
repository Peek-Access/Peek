namespace Peek.Worker.Llm;

public sealed class OllamaOptions
{
    /// <summary>Local by default (§16) - a remote Ollama endpoint is still "local-first" only in the sense that nothing else silently redirects it; pointing this at a remote host is an explicit user choice.</summary>
    public string Endpoint { get; set; } = "http://localhost:11434";

    public string Model { get; set; } = "llama3.2:latest";

    /// <summary>Generous default - a cold Ollama model load can take many seconds before the first token.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(60);
}
