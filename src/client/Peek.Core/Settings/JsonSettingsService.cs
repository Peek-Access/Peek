using System.Text.Json;
using Microsoft.Extensions.Logging;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace Peek.Core.Settings;

/// <summary>
/// Persists <see cref="PeekSettings"/> as indented JSON under the user's local app
/// data folder. Writes go through a temp-file-then-move so a crash mid-save can
/// never leave a half-written settings.json behind.
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    private readonly string _filePath;
    private readonly ILogger<JsonSettingsService> _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly BehaviorSignal<PeekSettings> _changes;

    public JsonSettingsService(ILogger<JsonSettingsService> logger)
        : this(DefaultFilePath, logger)
    {
    }

    /// <summary>Testing/advanced seam - lets a caller point persistence at a specific file instead of <see cref="DefaultFilePath"/>.</summary>
    public JsonSettingsService(string filePath, ILogger<JsonSettingsService> logger)
    {
        _filePath = filePath;
        _logger = logger;
        _changes = new BehaviorSignal<PeekSettings>(new PeekSettings());
    }

    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Peek", "settings.json");

    public PeekSettings Current => _changes.Value;

    public IObservable<PeekSettings> Changes => _changes.AsObservable();

    public void Load()
    {
        _fileLock.Wait();
        try
        {
            var settings = ReadFromDiskOrDefault();
            settings.Validate();
            _changes.OnNext(settings);

            if (!File.Exists(_filePath))
                WriteSync(settings);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            PeekSettings settings;
            if (File.Exists(_filePath))
            {
                try
                {
                    await using var stream = File.OpenRead(_filePath);
                    settings = await JsonSerializer.DeserializeAsync(stream, PeekSettingsJsonContext.Default.PeekSettings, ct)
                        .ConfigureAwait(false) ?? new PeekSettings();
                    settings = SettingsMigration.Apply(settings);
                }
                catch (Exception ex)
                {
                    // A corrupt/hand-edited settings.json should not prevent the app
                    // from starting - fall back to defaults rather than crash.
                    _logger.LogWarning(ex, "Failed to read settings from {Path} - using defaults", _filePath);
                    settings = new PeekSettings();
                }
            }
            else
            {
                settings = new PeekSettings();
            }

            settings.Validate();
            _changes.OnNext(settings);

            if (!File.Exists(_filePath))
                await WriteAsync(settings, ct).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private PeekSettings ReadFromDiskOrDefault()
    {
        if (!File.Exists(_filePath))
            return new PeekSettings();

        try
        {
            var json = File.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize(json, PeekSettingsJsonContext.Default.PeekSettings) ?? new PeekSettings();
            return SettingsMigration.Apply(settings);
        }
        catch (Exception ex)
        {
            // A corrupt/hand-edited settings.json should not prevent the app from
            // starting - fall back to defaults rather than crash.
            _logger.LogWarning(ex, "Failed to read settings from {Path} - using defaults", _filePath);
            return new PeekSettings();
        }
    }

    public Task SaveAsync(CancellationToken ct = default) => WriteWithLockAsync(Current, ct);

    public async Task UpdateAsync(Action<PeekSettings> mutate, CancellationToken ct = default)
    {
        var settings = Current;
        mutate(settings);
        settings.Validate();
        await WriteWithLockAsync(settings, ct).ConfigureAwait(false);
        _changes.OnNext(settings);
    }

    private async Task WriteWithLockAsync(PeekSettings settings, CancellationToken ct)
    {
        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await WriteAsync(settings, ct).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task WriteAsync(PeekSettings settings, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = $"{_filePath}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, PeekSettingsJsonContext.Default.PeekSettings, ct)
                .ConfigureAwait(false);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }

    private void WriteSync(PeekSettings settings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = $"{_filePath}.tmp";
        var json = JsonSerializer.Serialize(settings, PeekSettingsJsonContext.Default.PeekSettings);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
