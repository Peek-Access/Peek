using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Peek.Worker.Contracts.Tts;
using PiperSharp;
using PiperSharp.Models;

namespace Peek.Worker.Tts;

/// <summary>
/// <see cref="ITtsService"/> backed by PiperSharp (https://github.com/Lyx52/PiperSharp),
/// a local, offline neural TTS engine - no cloud speech synthesis (§2).
/// Owns a single-consumer queue so callers get queued speech with interruption
/// without blocking, and caches loaded <see cref="VoiceModel"/>s so repeat requests
/// for the same voice don't re-read model.json from disk (§25).
/// </summary>
public sealed class PiperTtsService : ITtsService, IAsyncDisposable
{
    private readonly Channel<QueuedUtterance> _queue = Channel.CreateUnbounded<QueuedUtterance>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly ConcurrentDictionary<string, VoiceModel> _modelCache = new();
    private readonly PiperTtsOptions _options;
    private readonly ILogger<PiperTtsService> _logger;
    private readonly string _piperDir;
    private readonly string _piperExecutablePath;
    private readonly string _modelsDir;
    private readonly CancellationTokenSource _serviceCts = new();
    private readonly Task _consumerLoop;

    private CancellationTokenSource _currentUtteranceCts = new();
    private long _utterancesSynthesized;
    private double _lastSynthesisMs;
    private string? _activeVoiceId;

    public PiperTtsService(IOptions<PiperTtsOptions> options, ILogger<PiperTtsService> logger)
    {
        _options = options.Value;
        _logger = logger;

        _piperDir = Path.Combine(_options.DataDirectory, "piper");
        _piperExecutablePath = Path.Combine(_piperDir, PiperDownloader.PiperExecutable);
        _modelsDir = Path.Combine(_options.DataDirectory, "models");

        // Must never throw out of this constructor: PiperTtsService is an eager
        // dependency of RpcRequestDispatcher, which the always-started WorkerPipeServer
        // depends on - an exception here previously crashed the entire worker process
        // before it ever opened its pipe (no UI Automation, no screenshot, nothing - not
        // just "no TTS"). If these directories genuinely can't be created, every
        // synthesis attempt fails on its own afterward with a clear, per-request error
        // (see EnsurePiperInstalledAsync/GetOrLoadVoiceModelAsync) instead of the whole
        // worker refusing to start.
        try
        {
            Directory.CreateDirectory(_piperDir);
            Directory.CreateDirectory(_modelsDir);
            SeedFromBundledAssets();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Piper data directories under '{DataDirectory}' - TTS will be unavailable until this is resolved", _options.DataDirectory);
        }

        _consumerLoop = Task.Run(() => ConsumeAsync(_serviceCts.Token));
    }

    /// <summary>
    /// Copies the pre-packaged Piper runtime and default voice models (see
    /// Peek.Worker.Tts.AssetBundler and PiperTtsOptions.BundledAssetsDirectory) from the
    /// read-only install folder into DataDirectory, once, if they're not already there - so a
    /// fresh install's very first announcement never has to wait on a network download for a
    /// voice Peek already shipped. Best-effort and synchronous (plain local file copies, not
    /// network I/O, so this doesn't reintroduce the kind of startup delay the DataDirectory
    /// creation above already guards against): a failed or partial copy just leaves the normal
    /// download-on-demand path (EnsurePiperInstalledAsync/GetOrLoadVoiceModelAsync) to handle
    /// it exactly as it always has, same as if no bundle existed at all. Every bundled voice
    /// folder is seeded, not just a hardcoded three, so bundling a fourth voice later needs no
    /// code change here.
    /// </summary>
    private void SeedFromBundledAssets()
    {
        if (_options.BundledAssetsDirectory is not { } bundledDir || !Directory.Exists(bundledDir))
            return;

        if (!File.Exists(_piperExecutablePath))
        {
            var bundledPiperDir = Path.Combine(bundledDir, "piper");
            if (Directory.Exists(bundledPiperDir))
            {
                _logger.LogInformation("Seeding Piper runtime from bundled assets at '{BundledDir}'", bundledDir);
                CopyDirectory(bundledPiperDir, _piperDir);
            }
        }

        var bundledModelsDir = Path.Combine(bundledDir, "models");
        if (!Directory.Exists(bundledModelsDir)) return;

        foreach (var bundledModelDir in Directory.GetDirectories(bundledModelsDir))
        {
            var voiceId = Path.GetFileName(bundledModelDir);
            var targetDir = Path.Combine(_modelsDir, voiceId);
            if (Directory.Exists(targetDir) && File.Exists(Path.Combine(targetDir, "model.json")))
                continue;

            _logger.LogInformation("Seeding voice model '{VoiceId}' from bundled assets", voiceId);
            CopyDirectory(bundledModelDir, targetDir);
        }
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: true);
        foreach (var subDir in Directory.GetDirectories(sourceDir))
            CopyDirectory(subDir, Path.Combine(targetDir, Path.GetFileName(subDir)));
    }

    public Task<IReadOnlyList<TtsVoiceInfo>> GetVoicesAsync(CancellationToken ct = default)
    {
        // Only locally-present (already downloaded) voices are listed - this never
        // reaches out to Hugging Face implicitly (local-first, §16).
        if (!Directory.Exists(_modelsDir))
            return Task.FromResult<IReadOnlyList<TtsVoiceInfo>>([]);

        var voices = Directory.GetDirectories(_modelsDir)
            .Select(Path.GetFileName)
            .Where(id => id is not null && File.Exists(Path.Combine(_modelsDir, id, "model.json")))
            .Select(id => new TtsVoiceInfo
            {
                Id = id!,
                Language = id!.Split('_', '-').FirstOrDefault() ?? "unknown",
                DisplayName = id,
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<TtsVoiceInfo>>(voices);
    }

    public async Task<TtsSpeakResult> SpeakAsync(TtsSpeakRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new ArgumentException("Text must not be empty.", nameof(request));

        if (request.Interrupt)
        {
            DrainQueue();
            await CancelCurrentUtteranceAsync().ConfigureAwait(false);
        }

        var item = new QueuedUtterance(
            request,
            new TaskCompletionSource<TtsSpeakResult>(TaskCreationOptions.RunContinuationsAsynchronously));

        using var reg = ct.Register(
            static state => ((TaskCompletionSource<TtsSpeakResult>)state!).TrySetCanceled(),
            item.Completion);

        await _queue.Writer.WriteAsync(item, ct).ConfigureAwait(false);
        return await item.Completion.Task.ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        DrainQueue();
        return CancelCurrentUtteranceAsync();
    }

    public Task<TtsStatus> GetStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new TtsStatus
        {
            IsReady = true,
            ActiveVoiceId = _activeVoiceId,
            QueueDepth = _queue.Reader.CanCount ? _queue.Reader.Count : 0,
            UtterancesSynthesized = Interlocked.Read(ref _utterancesSynthesized),
            LastSynthesisMs = _lastSynthesisMs,
        });

    private void DrainQueue()
    {
        while (_queue.Reader.TryRead(out var stale))
            stale.Completion.TrySetCanceled();
    }

    private async Task CancelCurrentUtteranceAsync()
    {
        var old = Interlocked.Exchange(ref _currentUtteranceCts, new CancellationTokenSource());
        try
        {
            await old.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            old.Dispose();
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _currentUtteranceCts.Token);
                try
                {
                    var result = await SynthesizeAsync(item.Request, linked.Token).ConfigureAwait(false);
                    item.Completion.TrySetResult(result);
                }
                catch (OperationCanceledException)
                {
                    item.Completion.TrySetCanceled();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "TTS synthesis failed");
                    item.Completion.TrySetException(ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Service shutting down.
        }
    }

    private async Task<TtsSpeakResult> SynthesizeAsync(TtsSpeakRequest request, CancellationToken ct)
    {
        var voiceId = string.IsNullOrWhiteSpace(request.VoiceId) ? _options.DefaultVoiceId : request.VoiceId;

        await EnsurePiperInstalledAsync(ct).ConfigureAwait(false);
        var model = await GetOrLoadVoiceModelAsync(voiceId, ct).ConfigureAwait(false);

        var config = new PiperConfiguration
        {
            ExecutableLocation = _piperExecutablePath,
            WorkingDirectory = _piperDir,
            Model = model,
            SpeakingRate = request.Rate <= 0 ? 1f : request.Rate,
            UseCuda = _options.UseCuda,
        };

        var provider = new PiperProvider(config);
        var sw = Stopwatch.StartNew();
        // Wav (not Mp3) so this project stays free of the Windows-only Media
        // Foundation encoder NAudio otherwise pulls in (§5 - TTS must stay
        // cross-platform even though the current host process targets Windows).
        var audio = await provider.InferAsync(request.Text, AudioOutputType.Wav, ct).ConfigureAwait(false);
        sw.Stop();

        Interlocked.Increment(ref _utterancesSynthesized);
        _lastSynthesisMs = sw.Elapsed.TotalMilliseconds;
        _activeVoiceId = voiceId;

        // Text content itself is never logged (§26 - only its length/timing).
        _logger.LogInformation(
            "TTS synthesized {Chars} chars in {Ms:F0}ms (voice={VoiceId})",
            request.Text.Length, sw.Elapsed.TotalMilliseconds, voiceId);

        var sampleRate = (int)(model.Audio?.SampleRate ?? 16000);
        return new TtsSpeakResult
        {
            AudioData = audio,
            Format = "wav",
            DurationMs = EstimateWavDurationMs(audio, sampleRate),
            SynthesisMs = sw.Elapsed.TotalMilliseconds,
        };
    }

    /// <summary>
    /// How long a first-run Piper runtime/voice download gets before this gives up on it for
    /// this attempt and lets FallbackTtsService switch to a Windows voice instead. PiperSharp's
    /// download calls take no CancellationToken of their own, so a dead/very slow connection
    /// would otherwise hang the caller's very first announcement for however long HttpClient's
    /// own default timeout is (~100s) - dead silence for a screen-reading tool's first real
    /// utterance is a far worse failure mode than falling back to a lesser voice quickly. The
    /// download keeps running in the background regardless (WaitAsync abandons the wait, not
    /// the task) and populates the on-disk cache for next time either way.
    /// </summary>
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(20);

    private async Task EnsurePiperInstalledAsync(CancellationToken ct)
    {
        if (File.Exists(_piperExecutablePath)) return;

        if (!_options.AutoDownloadAssets)
            throw new InvalidOperationException(
                $"Piper executable not found at '{_piperExecutablePath}' and AutoDownloadAssets is disabled.");

        _logger.LogInformation("Piper runtime not found locally - downloading to {Dir}", _piperDir);
        ct.ThrowIfCancellationRequested();
        // The release archive already contains a top-level "piper/" folder, so this
        // extracts into DataDirectory/piper/... (i.e. _piperDir) - matching
        // PiperDownloader's own DefaultPiperLocation convention. Extracting to
        // _piperDir itself would double-nest it as _piperDir/piper/piper.exe.
        await PiperDownloader.DownloadPiper().ExtractPiper(_options.DataDirectory)
            .WaitAsync(DownloadTimeout, ct).ConfigureAwait(false);
    }

    private async Task<VoiceModel> GetOrLoadVoiceModelAsync(string voiceId, CancellationToken ct)
    {
        if (_modelCache.TryGetValue(voiceId, out var cached))
            return cached;

        var modelDir = Path.Combine(_modelsDir, voiceId);
        VoiceModel model;

        if (Directory.Exists(modelDir) && File.Exists(Path.Combine(modelDir, "model.json")))
        {
            model = await VoiceModel.LoadModel(modelDir).ConfigureAwait(false);
        }
        else
        {
            if (!_options.AutoDownloadAssets)
                throw new InvalidOperationException(
                    $"Voice model '{voiceId}' not found locally at '{modelDir}' and AutoDownloadAssets is disabled.");

            _logger.LogInformation("Voice model '{VoiceId}' not found locally - downloading", voiceId);
            ct.ThrowIfCancellationRequested();
            var descriptor = await PiperDownloader.GetModelByKey(voiceId).WaitAsync(DownloadTimeout, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Unknown Piper voice model key '{voiceId}'.");
            model = await descriptor.DownloadModel(_modelsDir).WaitAsync(DownloadTimeout, ct).ConfigureAwait(false);
        }

        _modelCache[voiceId] = model;
        return model;
    }

    private static double EstimateWavDurationMs(byte[] wavBytes, int sampleRate)
    {
        const int HeaderSize = 44;
        const int BytesPerSample = 2; // 16-bit PCM mono, per RawSourceWaveStream's WaveFormat above
        if (sampleRate <= 0) return 0;
        var dataBytes = Math.Max(0, wavBytes.Length - HeaderSize);
        return dataBytes / (double)BytesPerSample / sampleRate * 1000.0;
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        await _serviceCts.CancelAsync().ConfigureAwait(false);
        try
        {
            await _consumerLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _serviceCts.Dispose();
        _currentUtteranceCts.Dispose();
    }

    private sealed record QueuedUtterance(TtsSpeakRequest Request, TaskCompletionSource<TtsSpeakResult> Completion);
}
