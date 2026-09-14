using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Peek.Worker.Contracts.Llm;

namespace Peek.Worker.Llm;

/// <summary>
/// <see cref="ILlmProvider"/> for Anthropic's Messages API (Claude). Its wire format
/// differs enough from the OpenAI-compatible family (a top-level "system" field instead of
/// a system-role message, a different vision content shape, named SSE events instead of
/// bare "data:" deltas) to warrant its own implementation rather than folding it into
/// OpenAiCompatibleProvider.
/// </summary>
public sealed class AnthropicProvider : ILlmProvider, IDisposable
{
    private const string ApiVersion = "2023-06-01";

    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicProvider> _logger;
    private readonly HttpClient _http;

    private long _requestsPerformed;
    private double _lastRequestMs;

    public string Name => "anthropic";

    public bool SupportsVision => true;

    public AnthropicProvider(IOptions<AnthropicOptions> options, ILogger<AnthropicProvider> logger)
    {
        _options = options.Value;
        _logger = logger;

        _http = new HttpClient
        {
            BaseAddress = new Uri(_options.Endpoint.TrimEnd('/') + "/"),
            Timeout = _options.RequestTimeout,
        };
        _http.DefaultRequestHeaders.Add("anthropic-version", ApiVersion);
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            _http.DefaultRequestHeaders.Add("x-api-key", _options.ApiKey);
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var payload = BuildRequest(request, stream: false);

        var sw = Stopwatch.StartNew();
        using var response = await _http
            .PostAsJsonAsync("v1/messages", payload, AnthropicJsonContext.Default.AnthropicRequest, ct)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

        var parsed = await response.Content
            .ReadFromJsonAsync(AnthropicJsonContext.Default.AnthropicResponse, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Anthropic returned an empty response.");
        sw.Stop();

        Interlocked.Increment(ref _requestsPerformed);
        _lastRequestMs = sw.Elapsed.TotalMilliseconds;
        _logger.LogInformation("Anthropic completion ({Model}) in {Ms:F0}ms", _options.Model, sw.Elapsed.TotalMilliseconds);

        var text = parsed.Content?.Where(c => c.Type == "text").Select(c => c.Text).FirstOrDefault(t => !string.IsNullOrEmpty(t))
            ?? string.Empty;
        return new LlmResponse { Text = text, DurationMs = sw.Elapsed.TotalMilliseconds };
    }

    public async IAsyncEnumerable<string> StreamAsync(LlmRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var payload = BuildRequest(request, stream: true);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = JsonContent.Create(payload, AnthropicJsonContext.Default.AnthropicRequest),
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
            if (payloadJson.Length == 0) continue;

            AnthropicStreamEvent? evt;
            try
            {
                evt = JsonSerializer.Deserialize(payloadJson, AnthropicJsonContext.Default.AnthropicStreamEvent);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse an Anthropic SSE chunk - skipping it");
                continue;
            }

            if (evt?.Type == "content_block_delta" && evt.Delta?.Text is { Length: > 0 } text)
                yield return text;
            else if (evt?.Type == "message_stop")
                break;
        }

        sw.Stop();
        Interlocked.Increment(ref _requestsPerformed);
        _lastRequestMs = sw.Elapsed.TotalMilliseconds;
        _logger.LogInformation("Anthropic stream completion ({Model}) in {Ms:F0}ms", _options.Model, sw.Elapsed.TotalMilliseconds);
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

    /// <summary>Anthropic takes the system prompt as its own top-level field, not a role="system" message - so any such messages are pulled out and concatenated here rather than sent in the messages array (the API rejects role="system").</summary>
    private AnthropicRequest BuildRequest(LlmRequest request, bool stream)
    {
        var systemPrompt = string.Join("\n\n", request.Messages.Where(m => m.Role == "system").Select(m => m.Content));
        var conversation = request.Messages.Where(m => m.Role != "system").Select(BuildMessage).ToList();

        return new AnthropicRequest
        {
            Model = _options.Model,
            System = systemPrompt.Length > 0 ? systemPrompt : null,
            Messages = conversation,
            Temperature = request.Temperature,
            MaxTokens = request.MaxTokens,
            Stream = stream,
        };
    }

    private static AnthropicMessage BuildMessage(LlmMessage m)
    {
        if (string.IsNullOrEmpty(m.ImageDataBase64))
            return new AnthropicMessage { Role = m.Role, Content = [new AnthropicContentPart { Type = "text", Text = m.Content }] };

        var mime = string.IsNullOrWhiteSpace(m.ImageMimeType) ? "image/png" : m.ImageMimeType;
        return new AnthropicMessage
        {
            Role = m.Role,
            Content =
            [
                new AnthropicContentPart { Type = "text", Text = m.Content },
                new AnthropicContentPart
                {
                    Type = "image",
                    Source = new AnthropicImageSource { Type = "base64", MediaType = mime, Data = m.ImageDataBase64 },
                },
            ],
        };
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new InvalidOperationException(
            $"Anthropic returned {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body, 500)}");
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
}

internal sealed class AnthropicRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("system")]
    public string? System { get; init; }

    [JsonPropertyName("messages")]
    public required List<AnthropicMessage> Messages { get; init; }

    [JsonPropertyName("temperature")]
    public float Temperature { get; init; }

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }
}

internal sealed class AnthropicMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public required List<AnthropicContentPart> Content { get; init; }
}

internal sealed class AnthropicContentPart
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("source")]
    public AnthropicImageSource? Source { get; init; }
}

internal sealed class AnthropicImageSource
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("media_type")]
    public required string MediaType { get; init; }

    [JsonPropertyName("data")]
    public required string Data { get; init; }
}

internal sealed class AnthropicResponse
{
    [JsonPropertyName("content")]
    public List<AnthropicResponseContent>? Content { get; init; }
}

internal sealed class AnthropicResponseContent
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

internal sealed class AnthropicStreamEvent
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("delta")]
    public AnthropicStreamDelta? Delta { get; init; }
}

internal sealed class AnthropicStreamDelta
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

[JsonSerializable(typeof(AnthropicRequest))]
[JsonSerializable(typeof(AnthropicResponse))]
[JsonSerializable(typeof(AnthropicStreamEvent))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class AnthropicJsonContext : JsonSerializerContext;
