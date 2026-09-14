using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Peek.Worker.Contracts.Llm;

namespace Peek.Worker.Llm;

/// <summary>
/// <see cref="ILlmProvider"/> backed by a local Ollama server
/// (https://ollama.com) - required for local models per §15, on by default per the
/// local-first principle (§16). Talks to Ollama's <c>/api/chat</c> endpoint
/// directly rather than an SDK, since the wire contract is small and stable.
/// </summary>
public sealed class OllamaProvider : ILlmProvider, IDisposable
{
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaProvider> _logger;
    private readonly HttpClient _http;

    private long _requestsPerformed;
    private double _lastRequestMs;

    public string Name => "ollama";

    // Wire-level only - whether it actually works depends on the chosen model being
    // multimodal (llava, llama3.2-vision, ...).
    public bool SupportsVision => true;

    public OllamaProvider(IOptions<OllamaOptions> options, ILogger<OllamaProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        _http = new HttpClient
        {
            BaseAddress = new Uri(_options.Endpoint),
            Timeout = _options.RequestTimeout,
        };
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        if (request.Messages.Count == 0)
            throw new ArgumentException("At least one message is required.", nameof(request));

        var payload = new OllamaChatRequest
        {
            Model = _options.Model,
            Messages = [.. request.Messages.Select(ToOllamaMessage)],
            Stream = false,
            Options = new OllamaRequestOptions { Temperature = request.Temperature, NumPredict = request.MaxTokens },
        };

        var sw = Stopwatch.StartNew();
        using var response = await _http
            .PostAsJsonAsync("/api/chat", payload, OllamaJsonContext.Default.OllamaChatRequest, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var parsed = await response.Content
            .ReadFromJsonAsync(OllamaJsonContext.Default.OllamaChatResponse, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Ollama returned an empty response.");
        sw.Stop();

        Interlocked.Increment(ref _requestsPerformed);
        _lastRequestMs = sw.Elapsed.TotalMilliseconds;

        // Prompt/response text is never logged (§26) - only model and timing.
        _logger.LogInformation("Ollama completion ({Model}) in {Ms:F0}ms", _options.Model, sw.Elapsed.TotalMilliseconds);

        return new LlmResponse
        {
            Text = parsed.Message?.Content ?? string.Empty,
            DurationMs = sw.Elapsed.TotalMilliseconds,
        };
    }

    public async IAsyncEnumerable<string> StreamAsync(LlmRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (request.Messages.Count == 0)
            throw new ArgumentException("At least one message is required.", nameof(request));

        var payload = new OllamaChatRequest
        {
            Model = _options.Model,
            Messages = [.. request.Messages.Select(ToOllamaMessage)],
            Stream = true,
            Options = new OllamaRequestOptions { Temperature = request.Temperature, NumPredict = request.MaxTokens },
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(payload, OllamaJsonContext.Default.OllamaChatRequest),
        };

        var sw = Stopwatch.StartNew();
        using var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line)) continue;

            OllamaChatResponse? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize(line, OllamaJsonContext.Default.OllamaChatResponse);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse an Ollama stream chunk - skipping it");
                continue;
            }

            if (chunk?.Message?.Content is { Length: > 0 } delta)
                yield return delta;

            if (chunk?.Done == true) break;
        }

        sw.Stop();
        Interlocked.Increment(ref _requestsPerformed);
        _lastRequestMs = sw.Elapsed.TotalMilliseconds;
        _logger.LogInformation("Ollama stream completion ({Model}) in {Ms:F0}ms", _options.Model, sw.Elapsed.TotalMilliseconds);
    }

    public async Task<LlmStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var isReady = false;
        try
        {
            using var response = await _http.GetAsync("/api/tags", ct).ConfigureAwait(false);
            isReady = response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ollama status check failed");
        }

        return new LlmStatus
        {
            IsReady = isReady,
            ProviderName = Name,
            Model = _options.Model,
            RequestsPerformed = Interlocked.Read(ref _requestsPerformed),
            LastRequestMs = _lastRequestMs,
        };
    }

    private static OllamaMessage ToOllamaMessage(LlmMessage m) => new()
    {
        Role = m.Role,
        Content = m.Content,
        Images = m.ImageDataBase64 is { Length: > 0 } image ? [image] : null,
    };

    public void Dispose() => _http.Dispose();
}
