using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Peek.Worker.Contracts.Llm;
using Peek.Worker.Contracts.Rpc;

namespace Peek.Worker.Llm;

/// <summary>
/// Resolves an <see cref="ILlmProvider"/> instance from a client-supplied
/// <see cref="LlmProviderConfig"/> - the worker is stateless with respect to AI settings
/// (§17: settings live client-side), so unlike every other worker service there is no
/// single startup-registered provider; every RPC call brings its own provider/endpoint/
/// credentials/model, and switching providers in Settings takes effect on the very next
/// call with no worker restart needed.
/// </summary>
public interface ILlmProviderFactory
{
    ILlmProvider GetProvider(LlmProviderConfig config);
}

public sealed class LlmProviderFactory : ILlmProviderFactory, IDisposable
{
    private readonly ILoggerFactory _loggerFactory;

    // Cached by full config (endpoint/model/key all matter) so repeated calls with the same
    // settings reuse one HttpClient instead of constructing a new one per request; a
    // provider is cheap to construct anyway (no network I/O until CompleteAsync/StreamAsync),
    // so this is purely a minor optimization, not a correctness requirement.
    private readonly ConcurrentDictionary<string, ILlmProvider> _cache = new();

    public LlmProviderFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public ILlmProvider GetProvider(LlmProviderConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Provider))
            throw new RpcMethodException(RpcErrorCodes.InvalidParams, "provider is required");
        if (string.IsNullOrWhiteSpace(config.Model))
            throw new RpcMethodException(RpcErrorCodes.InvalidParams, "model is required");

        var key = string.Join('|', config.Provider, config.Endpoint, config.Model, config.ApiKey);
        return _cache.GetOrAdd(key, _ => CreateProvider(config));
    }

    private ILlmProvider CreateProvider(LlmProviderConfig config) => config.Provider.ToLowerInvariant() switch
    {
        "ollama" => new OllamaProvider(
            Options.Create(new OllamaOptions
            {
                Endpoint = string.IsNullOrWhiteSpace(config.Endpoint) ? "http://localhost:11434" : config.Endpoint,
                Model = config.Model,
            }),
            _loggerFactory.CreateLogger<OllamaProvider>()),

        "openai" => new OpenAiCompatibleProvider(
            Options.Create(new OpenAiCompatibleOptions
            {
                Endpoint = string.IsNullOrWhiteSpace(config.Endpoint) ? "https://api.openai.com/v1" : config.Endpoint,
                ApiKey = config.ApiKey,
                Model = config.Model,
            }),
            _loggerFactory.CreateLogger<OpenAiCompatibleProvider>(),
            providerName: "openai"),

        "openrouter" => new OpenAiCompatibleProvider(
            Options.Create(new OpenAiCompatibleOptions
            {
                Endpoint = string.IsNullOrWhiteSpace(config.Endpoint) ? "https://openrouter.ai/api/v1" : config.Endpoint,
                ApiKey = config.ApiKey,
                Model = config.Model,
            }),
            _loggerFactory.CreateLogger<OpenAiCompatibleProvider>(),
            providerName: "openrouter"),

        // Any other OpenAI-compatible endpoint the user points us at directly - we only
        // speak the OpenAI /chat/completions shape to it; whether the real server behind
        // that URL is actually compatible is the user's call, not ours to police.
        "custom" => new OpenAiCompatibleProvider(
            Options.Create(new OpenAiCompatibleOptions
            {
                Endpoint = string.IsNullOrWhiteSpace(config.Endpoint)
                    ? throw new RpcMethodException(RpcErrorCodes.InvalidParams, "endpoint is required for the custom provider")
                    : config.Endpoint,
                ApiKey = config.ApiKey,
                Model = config.Model,
            }),
            _loggerFactory.CreateLogger<OpenAiCompatibleProvider>(),
            providerName: "custom"),

        "anthropic" => new AnthropicProvider(
            Options.Create(new AnthropicOptions
            {
                Endpoint = string.IsNullOrWhiteSpace(config.Endpoint) ? "https://api.anthropic.com" : config.Endpoint,
                ApiKey = config.ApiKey,
                Model = config.Model,
            }),
            _loggerFactory.CreateLogger<AnthropicProvider>()),

        "gemini" => new GeminiProvider(
            Options.Create(new GeminiOptions
            {
                Endpoint = string.IsNullOrWhiteSpace(config.Endpoint) ? "https://generativelanguage.googleapis.com" : config.Endpoint,
                ApiKey = config.ApiKey,
                Model = config.Model,
            }),
            _loggerFactory.CreateLogger<GeminiProvider>()),

        _ => throw new RpcMethodException(RpcErrorCodes.InvalidParams, $"Unknown LLM provider '{config.Provider}'"),
    };

    public void Dispose()
    {
        foreach (var provider in _cache.Values)
            (provider as IDisposable)?.Dispose();
    }
}
