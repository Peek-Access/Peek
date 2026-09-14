using Peek.Worker.Contracts.Llm;
using Peek.Core.Settings;

namespace Peek.Core.Services.Llm;

/// <summary>
/// Resolves AiSettings' active provider into the wire-ready LlmProviderConfig every llm.*
/// RPC call needs (the worker is stateless with respect to AI settings - see
/// LlmProviderFactory on the worker). Also the one place PrivacySettings.AllowRemoteLlm is
/// enforced (§16: sending data to a remote service is explicit opt-in) - every caller of
/// ILlmClient.CompleteAsync/CompleteStreamAsync goes through this rather than reading
/// AiSettings.Providers directly, so that gate can't accidentally be bypassed by a new call
/// site.
/// </summary>
public static class LlmProviderConfigResolver
{
    public static LlmProviderConfig Resolve(PeekSettings settings)
    {
        var providerId = settings.Ai.Provider;
        if (!settings.Ai.Providers.TryGetValue(providerId, out var credentials))
            throw new InvalidOperationException($"Unknown AI provider '{providerId}' - check Settings > AI.");

        if (!string.Equals(providerId, "ollama", StringComparison.OrdinalIgnoreCase) && !settings.Privacy.AllowRemoteLlm)
            throw new InvalidOperationException(
                "This provider sends data to a remote service, which is disabled in Settings > Privacy > Allow remote AI.");

        return new LlmProviderConfig
        {
            Provider = providerId,
            Endpoint = string.IsNullOrWhiteSpace(credentials.Endpoint) ? null : credentials.Endpoint,
            ApiKey = credentials.ApiKey,
            Model = credentials.Model,
        };
    }
}
