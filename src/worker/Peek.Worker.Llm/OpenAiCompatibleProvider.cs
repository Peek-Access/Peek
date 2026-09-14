using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Peek.Worker.Contracts.Llm;

namespace Peek.Worker.Llm;

/// <summary>
/// <see cref="ILlmProvider"/> for any OpenAI-compatible <c>/chat/completions</c> endpoint -
/// covers OpenAI, OpenRouter, and any user-supplied "custom" endpoint (all three speak the
/// same wire format, just with a different base URL/key/model - see LlmProviderFactory),
/// plus any locally-hosted OpenAI-compatible server. Per §15/§16 remote providers are
/// explicit opt-in: the client only ever sends one of these configs when the user has
/// actually selected that provider in Settings, never implicitly.
/// </summary>
public sealed class OpenAiCompatibleProvider : ILlmProvider, IDisposable
{
    private readonly OpenAiCompatibleOptions _options;
    private readonly ILogger<OpenAiCompatibleProvider> _logger;
    private readonly HttpClient _http;

    private long _requestsPerformed;
    private double _lastRequestMs;

    public string Name { get; }

    public bool SupportsVision { get; }

    public OpenAiCompatibleProvider(
        IOptions<OpenAiCompatibleOptions> options,
        ILogger<OpenAiCompatibleProvider> logger,
        string providerName = "openai-compatible",
        bool supportsVision = true)
    {
        _options = options.Value;
        _logger = logger;
        Name = providerName;
        SupportsVision = supportsVision;

        if (string.IsNullOrWhiteSpace(_options.Endpoint))
            throw new InvalidOperationException(
                $"{nameof(OpenAiCompatibleOptions)}.{nameof(OpenAiCompatibleOptions.Endpoint)} must be set explicitly - this provider is never enabled implicitly (§16).");

        _http = new HttpClient
        {
            BaseAddress = new Uri(_options.Endpoint.TrimEnd('/') + "/"),
            Timeout = _options.RequestTimeout,
        };
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        if (request.Messages.Count == 0)
            throw new ArgumentException("At least one message is required.", nameof(request));

        var payload = BuildRequest(request, stream: false);

        var sw = Stopwatch.StartNew();
        using var response = await _http
            .PostAsJsonAsync("chat/completions", payload, OpenAiJsonContext.Default.OpenAiChatRequest, ct)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

        var parsed = await response.Content
            .ReadFromJsonAsync(OpenAiJsonContext.Default.OpenAiChatResponse, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The remote provider returned an empty response.");
        sw.Stop();

        Interlocked.Increment(ref _requestsPerformed);
        _lastRequestMs = sw.Elapsed.TotalMilliseconds;

        // Prompt/response text is never logged (§26) - only model and timing.
        _logger.LogInformation("{Provider} completion ({Model}) in {Ms:F0}ms", Name, _options.Model, sw.Elapsed.TotalMilliseconds);

        return new LlmResponse
        {
            Text = parsed.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty,
            DurationMs = sw.Elapsed.TotalMilliseconds,
        };
    }

    public async IAsyncEnumerable<string> StreamAsync(LlmRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (request.Messages.Count == 0)
            throw new ArgumentException("At least one message is required.", nameof(request));

        var payload = BuildRequest(request, stream: true);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload, OpenAiJsonContext.Default.OpenAiChatRequest),
        };

        var sw = Stopwatch.StartNew();
        using var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null || !line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var payloadJson = line["data:".Length..].Trim();
            if (payloadJson is "" or "[DONE]") continue;

            OpenAiStreamChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize(payloadJson, OpenAiJsonContext.Default.OpenAiStreamChunk);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse an SSE chunk from {Provider} - skipping it", Name);
                continue;
            }

            var delta = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
            if (!string.IsNullOrEmpty(delta))
                yield return delta;
        }

        sw.Stop();
        Interlocked.Increment(ref _requestsPerformed);
        _lastRequestMs = sw.Elapsed.TotalMilliseconds;
        _logger.LogInformation("{Provider} stream completion ({Model}) in {Ms:F0}ms", Name, _options.Model, sw.Elapsed.TotalMilliseconds);
    }

    public Task<LlmStatus> GetStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new LlmStatus
        {
            IsReady = true,
            ProviderName = Name,
            Model = _options.Model,
            RequestsPerformed = Interlocked.Read(ref _requestsPerformed),
            LastRequestMs = _lastRequestMs,
        });

    public void Dispose() => _http.Dispose();

    private OpenAiChatRequest BuildRequest(LlmRequest request, bool stream) => new()
    {
        Model = _options.Model,
        Messages = [.. request.Messages.Select(BuildMessage)],
        Temperature = request.Temperature,
        MaxTokens = request.MaxTokens,
        Stream = stream,
    };

    private static OpenAiMessage BuildMessage(LlmMessage m) => new()
    {
        Role = m.Role,
        Content = BuildContent(m),
    };

    private static JsonElement BuildContent(LlmMessage m)
    {
        if (string.IsNullOrEmpty(m.ImageDataBase64))
            return JsonSerializer.SerializeToElement(m.Content, OpenAiJsonContext.Default.String);

        var mime = string.IsNullOrWhiteSpace(m.ImageMimeType) ? "image/png" : m.ImageMimeType;
        List<OpenAiContentPart> parts =
        [
            new() { Type = "text", Text = m.Content },
            new() { Type = "image_url", ImageUrl = new OpenAiImageUrl { Url = $"data:{mime};base64,{m.ImageDataBase64}" } },
        ];
        return JsonSerializer.SerializeToElement(parts, OpenAiJsonContext.Default.ListOpenAiContentPart);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new InvalidOperationException(
            $"Remote provider returned {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body, 500)}");
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
}

internal sealed class OpenAiChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required List<OpenAiMessage> Messages { get; init; }

    [JsonPropertyName("temperature")]
    public float Temperature { get; init; }

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }
}

internal sealed class OpenAiMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>Either a plain JSON string (text-only) or a content-part array (vision) - see BuildContent.</summary>
    [JsonPropertyName("content")]
    public required JsonElement Content { get; init; }
}

internal sealed class OpenAiContentPart
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("image_url")]
    public OpenAiImageUrl? ImageUrl { get; init; }
}

internal sealed class OpenAiImageUrl
{
    [JsonPropertyName("url")]
    public required string Url { get; init; }
}

internal sealed class OpenAiChatResponse
{
    [JsonPropertyName("choices")]
    public List<OpenAiResponseChoice>? Choices { get; init; }
}

internal sealed class OpenAiResponseChoice
{
    [JsonPropertyName("message")]
    public OpenAiResponseMessage? Message { get; init; }
}

internal sealed class OpenAiResponseMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

internal sealed class OpenAiStreamChunk
{
    [JsonPropertyName("choices")]
    public List<OpenAiStreamChoice>? Choices { get; init; }
}

internal sealed class OpenAiStreamChoice
{
    [JsonPropertyName("delta")]
    public OpenAiDelta? Delta { get; init; }
}

internal sealed class OpenAiDelta
{
    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

[JsonSerializable(typeof(OpenAiChatRequest))]
[JsonSerializable(typeof(OpenAiChatResponse))]
[JsonSerializable(typeof(OpenAiStreamChunk))]
[JsonSerializable(typeof(List<OpenAiContentPart>))]
[JsonSerializable(typeof(string))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class OpenAiJsonContext : JsonSerializerContext;
