using System.Reflection;

namespace Peek.Core.Services;

/// <summary>
/// Plays a short, embedded notification chime through <see cref="AudioPlayer"/> - the
/// same cross-platform-capable (LibVLC-backed) playback path TTS audio already uses,
/// deliberately not System.Media.SoundPlayer/SystemSounds (Windows-only BCL APIs) since
/// this app may target macOS in the future. The chime is distinct from TTS on purpose:
/// it's the audible cue that "a window-change announcement is coming next", not the
/// announcement itself.
/// </summary>
public sealed class NotificationSoundPlayer
{
    private const string ResourceFileName = "window-announcement.wav";

    private readonly AudioPlayer _audioPlayer;
    private byte[]? _soundBytes;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public NotificationSoundPlayer(AudioPlayer audioPlayer)
    {
        _audioPlayer = audioPlayer;
    }

    /// <summary>Plays the chime and returns once playback has finished (so a caller can reliably speak right after).</summary>
    public async Task PlayAsync()
    {
        var bytes = await GetSoundBytesAsync().ConfigureAwait(false);
        await _audioPlayer.VlcInitializeAsync().ConfigureAwait(false);
        await _audioPlayer.PlayBytesAsync(bytes, "wav").ConfigureAwait(false);
    }

    private async Task<byte[]> GetSoundBytesAsync()
    {
        if (_soundBytes is { } cached) return cached;

        await _loadLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_soundBytes is { } alreadyLoaded) return alreadyLoaded;

            var assembly = typeof(NotificationSoundPlayer).Assembly;
            var resourceName = Array.Find(assembly.GetManifestResourceNames(),
                n => n.EndsWith(ResourceFileName, StringComparison.Ordinal));

            if (resourceName is null)
                throw new InvalidOperationException(
                    $"Embedded notification sound '{ResourceFileName}' not found in {assembly.GetName().Name} " +
                    "(expected an <EmbeddedResource> in the csproj).");

            await using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer).ConfigureAwait(false);

            _soundBytes = buffer.ToArray();
            return _soundBytes;
        }
        finally
        {
            _loadLock.Release();
        }
    }
}
