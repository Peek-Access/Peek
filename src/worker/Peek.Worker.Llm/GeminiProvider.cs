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
/// <see cref="ILlmProvider"/> for Google's Gemini <c>generateContent</c> API. Its wire
/// format differs from both the OpenAI-compatible family and Anthropic (a top-level
/// "system_instruction" field, "model" instead of "assistant" as the reply role, an
/// API-key query parameter instead of a header, base64 images as "inline_data") - kept as
/// its own implementation rather than shoehorned into one of the others.
/// </summary>
public sealed class GeminiProvider : ILlmProvider, IDisposable
{
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiProvider> _logger;
    private readonly HttpClient _http;

    private long _requestsPerformed;
    private double _lastRequestMs;

    public string Name => "gemini";

    public bool SupportsVision => true;

    public GeminiProvider(IOptions<GeminiOptions> options, ILogger<GeminiProvider> logger)
    {
        _options = options.Value;
        _logger = logger;

        _http = new HttpClient
        {
            BaseAddress = new Uri(_options.Endpoint.TrimEnd('/') + "/"),
            Timeout = _options.RequestTimeout,
        };
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var payload = BuildRequest(request);
        var url = $"v1beta/models/{Uri.EscapeDataString(_options.Model)}:generateContent?key={Uri.EscapeDataString(_options.ApiKey ?? "")}";

        var sw = Stopwatch.StartNew();
        using var response = await _http
            .PostAsJsonAsync(url, payload, GeminiJsonContext.Default.GeminiRequest, ct)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

        var parsed = await response.Content
            .ReadFromJsonAsync(GeminiJsonContext.Default.GeminiResponse, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Gemini returned an empty response.");
        sw.Stop();

        Interlocked.Increment(ref _requestsPerformed);
        _lastRequestMs = sw.Elapsed.TotalMilliseconds;
        _logger.LogInformation("Gemini completion ({Model}) in {Ms:F0}ms", _options.Model, sw.Elapsed.TotalMilliseconds);

        var text = ExtractText(parsed);
        return new LlmResponse { Text = text, DurationMs = sw.Elapsed.TotalMilliseconds };
    }

    public async IAsyncEnumerable<string> StreamAsync(LlmRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var payload = BuildRequest(request);
        var url = $"v1beta/models/{Uri.EscapeDataString(_options.Model)}:streamGenerateContent?alt=sse&key={Uri.EscapeDataString(_options.ApiKey ?? "")}";

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload, GeminiJsonContext.Default.GeminiRequest),
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

            GeminiResponse? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize(payloadJson, GeminiJsonContext.Default.GeminiResponse);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse a Gemini SSE chunk - skipping it");
                continue;
            }

            var delta = chunk is null ? null : ExtractText(chunk);
            if (!string.IsNullOrEmpty(delta))
                yield return delta;
        }

        sw.Stop();
        Interlocked.Increment(ref _requestsPerformed);
        _lastRequestMs = sw.Elapsed.TotalMilliseconds;
        _logger.LogInformation("Gemini stream completion ({Model}) in {Ms:F0}ms", _options.Model, sw.Elapsed.TotalMilliseconds);
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

    /// <summary>Gemini takes the system prompt as its own top-level field, not a role="system" entry in "contents" - so any such messages are pulled out and concatenated here.</summary>
    private static GeminiRequest BuildRequest(LlmRequest request)
    {
        var systemPrompt = string.Join("\n\n", request.Messages.Where(m => m.Role == "system").Select(m => m.Content));
        var conversation = request.Messages.Where(m => m.Role != "system").Select(BuildContent).ToList();

        return new GeminiRequest
        {
            SystemInstruction = systemPrompt.Length > 0 ? new GeminiContent { Role = null, Parts = [new GeminiPart { Text = systemPrompt }] } : null,
            Contents = conversation,
            GenerationConfig = new GeminiGenerationConfig { Temperature = request.Temperature, MaxOutputTokens = request.MaxTokens },
        };
    }

    private static GeminiContent BuildContent(LlmMessage m)
    {
        // Gemini calls the assistant role "model", not "assistant".
        var role = m.Role == "assistant" ? "model" : "user";
        var parts = new List<GeminiPart> { new() { Text = m.Content } };

        if (!string.IsNullOrEmpty(m.ImageDataBase64))
        {
            var mime = string.IsNullOrWhiteSpace(m.ImageMimeType) ? "image/png" : m.ImageMimeType;
            parts.Add(new GeminiPart { InlineData = new GeminiInlineData { MimeType = mime, Data = m.ImageDataBase64 } });
        }

        return new GeminiContent { Role = role, Parts = parts };
    }

    private static string ExtractText(GeminiResponse response) =>
        response.Candidates?.FirstOrDefault()?.Content?.Parts?
            .Where(p => !string.IsNullOrEmpty(p.Text))
            .Select(p => p.Text!)
            .FirstOrDefault() ?? string.Empty;

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new InvalidOperationException(
            $"Gemini returned {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body, 500)}");
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
}

internal sealed class GeminiRequest
{
    [JsonPropertyName("system_instruction")]
    public GeminiContent? SystemInstruction { get; init; }

    [JsonPropertyName("contents")]
    public required List<GeminiContent> Contents { get; init; }

    [JsonPropertyName("generationConfig")]
    public GeminiGenerationConfig? GenerationConfig { get; init; }
}

internal sealed class GeminiContent
{
    [JsonPropertyName("role")]
    public string? Role { get; init; }

    [JsonPropertyName("parts")]
    public required List<GeminiPart> Parts { get; init; }
}

internal sealed class GeminiPart
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("inline_data")]
    public GeminiInlineData? InlineData { get; init; }
}

internal sealed class GeminiInlineData
{
    [JsonPropertyName("mime_type")]
    public required string MimeType { get; init; }

    [JsonPropertyName("data")]
    public required string Data { get; init; }
}

internal sealed class GeminiGenerationConfig
{
    [JsonPropertyName("temperature")]
    public float Temperature { get; init; }

    [JsonPropertyName("maxOutputTokens")]
    public int MaxOutputTokens { get; init; }
}

internal sealed class GeminiResponse
{
    [JsonPropertyName("candidates")]
    public List<GeminiCandidate>? Candidates { get; init; }
}

internal sealed class GeminiCandidate
{
    [JsonPropertyName("content")]
    public GeminiContent? Content { get; init; }
}

[JsonSerializable(typeof(GeminiRequest))]
[JsonSerializable(typeof(GeminiResponse))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class GeminiJsonContext : JsonSerializerContext;
