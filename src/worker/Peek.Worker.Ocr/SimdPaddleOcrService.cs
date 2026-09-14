using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Peek.Worker.Contracts.Ocr;
using Sdcb.SimdPaddleOCR;
using Sdcb.SimdPaddleOCR.Models.ChineseV6Tiny;
using SkiaSharp;

namespace Peek.Worker.Ocr;

/// <summary>
/// <see cref="IOcrService"/> backed by Sdcb.SimdPaddleOCR
/// (https://github.com/sdcb/SimdPaddleOCR) - a pure-managed, local, offline OCR
/// pipeline (no OpenCV/native ONNX runtime dependency). The PP-OCRv6-tiny model is
/// embedded in the Sdcb.SimdPaddleOCR.Models.ChineseV6Tiny package itself, so unlike
/// PiperSharp's TTS runtime, OCR needs no first-use network fetch at all (§16).
/// </summary>
public sealed class SimdPaddleOcrService : IOcrService, IAsyncDisposable
{
    private readonly PeekOcrOptions _options;
    private readonly ILogger<SimdPaddleOcrService> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    private PaddleOcrAll? _ocr;
    private long _recognitionsPerformed;
    private long _cacheHits;
    private double _lastRecognitionMs;

    public SimdPaddleOcrService(IOptions<PeekOcrOptions> options, ILogger<SimdPaddleOcrService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken ct = default)
    {
        if (request.ImageData.Length == 0)
            throw new ArgumentException("ImageData must not be empty.", nameof(request));

        var cacheKey = ComputeCacheKey(request);
        if (_cache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.CreatedUtc < _options.CacheTtl)
        {
            Interlocked.Increment(ref _cacheHits);
            return new OcrResult
            {
                Lines = cached.Result.Lines,
                Text = cached.Result.Text,
                RecognitionMs = cached.Result.RecognitionMs,
                FromCache = true,
            };
        }

        var ocr = await EnsureLoadedAsync(ct).ConfigureAwait(false);
        var (bgr, width, height, stride) = DecodeToBgr(request.ImageData, request.Region);

        var sw = Stopwatch.StartNew();
        // PaddleOcrAll.Run has no cancellation hook of its own; a screen-region
        // crop is typically sub-second, so this checks before/after rather than
        // trying to abort mid-inference (same tradeoff as Piper's subprocess).
        ct.ThrowIfCancellationRequested();
        var raw = await Task.Run(() => ocr.Run(bgr, width, height, stride), ct).ConfigureAwait(false);
        sw.Stop();

        var result = new OcrResult
        {
            Lines = [.. raw.Lines.Select(l => new OcrLine
            {
                Text = l.Text,
                Confidence = l.RecognitionScore,
                Box = new OcrQuad(l.Box.X1, l.Box.Y1, l.Box.X2, l.Box.Y2, l.Box.X3, l.Box.Y3, l.Box.X4, l.Box.Y4),
            })],
            Text = raw.Text,
            RecognitionMs = sw.Elapsed.TotalMilliseconds,
        };

        Interlocked.Increment(ref _recognitionsPerformed);
        _lastRecognitionMs = sw.Elapsed.TotalMilliseconds;

        // Recognized text is never logged (§26 - screenshots/OCR text are private
        // UI content) - only line count and timing.
        _logger.LogInformation(
            "OCR recognized {Lines} line(s) from a {W}x{H} image in {Ms:F0}ms",
            result.Lines.Count, width, height, sw.Elapsed.TotalMilliseconds);

        TrimCache();
        _cache[cacheKey] = new CacheEntry(result, DateTime.UtcNow);
        return result;
    }

    public Task<OcrStatus> GetStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new OcrStatus
        {
            IsReady = _ocr is not null,
            ModelName = "PP-OCRv6-tiny",
            RecognitionsPerformed = Interlocked.Read(ref _recognitionsPerformed),
            CacheHits = Interlocked.Read(ref _cacheHits),
            LastRecognitionMs = _lastRecognitionMs,
        });

    private async Task<PaddleOcrAll> EnsureLoadedAsync(CancellationToken ct)
    {
        if (_ocr is { } loaded) return loaded;

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_ocr is { } alreadyLoaded) return alreadyLoaded;

            _logger.LogInformation("Loading OCR model (PP-OCRv6-tiny, embedded)...");
            var engineOptions = new PaddleOcrOptions
            {
                UseDirectionClassification = _options.UseDirectionClassification,
            };
            _ocr = await PaddleOcrAll.LoadAsync(ChineseV6TinyModels.Default, engineOptions, ct).ConfigureAwait(false);
            _logger.LogInformation("OCR model loaded");
            return _ocr;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static (byte[] Bgr, int Width, int Height, int Stride) DecodeToBgr(byte[] encodedImage, OcrRegion? region)
    {
        using var decoded = SKBitmap.Decode(encodedImage)
            ?? throw new InvalidDataException("Could not decode OCR image (expected PNG).");

        using var normalized = new SKBitmap(new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        if (!decoded.CopyTo(normalized, SKColorType.Bgra8888))
            throw new InvalidDataException("Failed to normalize OCR image pixel format.");

        var (left, top, width, height) = ClampRegion(region, normalized.Width, normalized.Height);
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Region does not overlap the image.", nameof(region));

        var stride = checked(width * 3);
        var bgr = new byte[checked(stride * height)];

        var srcStride = normalized.RowBytes;
        var srcSpan = normalized.GetPixelSpan();

        for (var y = 0; y < height; y++)
        {
            var srcRowStart = (top + y) * srcStride + (left * 4);
            var dstRowStart = y * stride;
            for (var x = 0; x < width; x++)
            {
                var s = srcRowStart + (x * 4);
                var d = dstRowStart + (x * 3);
                bgr[d] = srcSpan[s];         // B
                bgr[d + 1] = srcSpan[s + 1]; // G
                bgr[d + 2] = srcSpan[s + 2]; // R
            }
        }

        return (bgr, width, height, stride);
    }

    private static (int Left, int Top, int Width, int Height) ClampRegion(OcrRegion? region, int imageWidth, int imageHeight)
    {
        if (region is null) return (0, 0, imageWidth, imageHeight);

        var left = Math.Clamp(region.X, 0, imageWidth);
        var top = Math.Clamp(region.Y, 0, imageHeight);
        var right = Math.Clamp(region.X + region.Width, left, imageWidth);
        var bottom = Math.Clamp(region.Y + region.Height, top, imageHeight);
        return (left, top, right - left, bottom - top);
    }

    private static string ComputeCacheKey(OcrRequest request)
    {
        Span<byte> regionBytes = stackalloc byte[16];
        if (request.Region is { } r)
        {
            BitConverter.TryWriteBytes(regionBytes[..4], r.X);
            BitConverter.TryWriteBytes(regionBytes[4..8], r.Y);
            BitConverter.TryWriteBytes(regionBytes[8..12], r.Width);
            BitConverter.TryWriteBytes(regionBytes[12..], r.Height);
        }
        else
        {
            regionBytes.Clear();
        }

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(request.ImageData);
        sha.AppendData(regionBytes);
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    private void TrimCache()
    {
        var cutoff = DateTime.UtcNow - _options.CacheTtl;
        foreach (var (key, entry) in _cache)
        {
            if (entry.CreatedUtc < cutoff)
                _cache.TryRemove(key, out _);
        }

        while (_cache.Count >= _options.MaxCacheEntries)
        {
            var oldest = _cache.OrderBy(kvp => kvp.Value.CreatedUtc).FirstOrDefault();
            if (oldest.Key is null) break;
            _cache.TryRemove(oldest.Key, out _);
        }
    }

    public ValueTask DisposeAsync()
    {
        _ocr?.Dispose();
        _initLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed record CacheEntry(OcrResult Result, DateTime CreatedUtc);
}
