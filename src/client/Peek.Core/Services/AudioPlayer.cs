using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;

namespace Peek.Core.Services;

public sealed class AudioPlayer : IDisposable
{
    /// <summary>
    /// Upper bound on how long one clip is allowed to keep <see cref="PlayFileAsync"/>
    /// pending. This is a wedge-breaker, not a real duration limit: Peek's clips are single
    /// spoken announcements, none of which run anywhere near this long.
    /// </summary>
    private static readonly TimeSpan MaxClipDuration = TimeSpan.FromMinutes(2);

    private readonly ILogger<AudioPlayer> _logger;

    private LibVLC? _libVlc;
    private volatile bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public MediaPlayer? MediaPlayer { get; private set; }

    public AudioPlayer(ILogger<AudioPlayer> logger)
    {
        _logger = logger;
    }

    public async Task VlcInitializeAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            await Task.Run(() =>
            {
                // Logged rather than silently thrown into a fire-and-forget task: this is the
                // single point of failure for every sound Peek makes, and the packaged build
                // ships a deliberately pruned set of LibVLC plugins (see release.yml), so "no
                // audio at all" is a plausible packaging regression that must be greppable in
                // the log instead of presenting as unexplained silence.
                _libVlc = new LibVLC();
                MediaPlayer = new MediaPlayer(_libVlc);
                _initialized = true;
                _logger.LogInformation("LibVLC initialized (version {Version})", _libVlc.Version);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "LibVLC failed to initialize - Peek will not be able to play any audio, " +
                "including speech. This usually means the libvlc folder or its plugins are missing.");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public Task PlayFileAsync(string filePath, CancellationToken ct = default)
    {
        if (MediaPlayer is null || _libVlc is null)
            throw new InvalidOperationException("Call VlcInitializeAsync first.");

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<EventArgs>? endHandler = null;
        EventHandler<EventArgs>? errorHandler = null;
        EventHandler<EventArgs>? stoppedHandler = null;
        CancellationTokenRegistration ctReg = default;
        CancellationTokenSource? timeoutCts = null;
        var finished = 0;

        // Exactly one of the completion paths below gets to finish the task, and whichever
        // does must also detach every handler. Without this, each interrupted announcement
        // used to leave its handlers attached to the shared MediaPlayer forever - Stop()
        // raises neither EndReached nor EncounteredError, so an interrupted clip (which
        // during fast mouse movement is nearly all of them) completed nothing and cleaned up
        // nothing.
        void Finish(Action complete)
        {
            // Several of these paths can fire at once (a Stopped right as the timeout
            // elapses, say); the first one owns the cleanup.
            if (Interlocked.Exchange(ref finished, 1) != 0) return;

            MediaPlayer.EndReached -= endHandler;
            MediaPlayer.EncounteredError -= errorHandler;
            MediaPlayer.Stopped -= stoppedHandler;
            ctReg.Dispose();
            timeoutCts?.Dispose();
            TryDeleteFile(filePath);
            complete();
        }

        endHandler = (_, _) => Finish(() => tcs.TrySetResult(true));

        errorHandler = (_, _) => Finish(() =>
        {
            _logger.LogWarning("LibVLC reported a playback error for {File}", Path.GetFileName(filePath));
            tcs.TrySetException(new InvalidOperationException("LibVLC playback error"));
        });

        // Stop() is how an announcement gets superseded by a newer one. Treating that as a
        // normal completion is what keeps the caller's await from hanging for the full
        // timeout on every interruption.
        stoppedHandler = (_, _) => Finish(() => tcs.TrySetResult(false));

        MediaPlayer.EndReached += endHandler;
        MediaPlayer.EncounteredError += errorHandler;
        MediaPlayer.Stopped += stoppedHandler;

        ctReg = ct.Register(() => Finish(() => tcs.TrySetCanceled(ct)));

        timeoutCts = new CancellationTokenSource(MaxClipDuration);
        timeoutCts.Token.Register(() => Finish(() =>
        {
            // Reaching here means LibVLC accepted the media and then went quiet - no
            // EndReached, no error, no Stopped. Completing anyway keeps one bad clip from
            // permanently blocking the speech pipeline, which is the difference between one
            // missed announcement and an app that never speaks again.
            _logger.LogWarning(
                "Playback of {File} produced no end/error event within {Timeout} - giving up on it",
                Path.GetFileName(filePath), MaxClipDuration);
            tcs.TrySetResult(false);
        }));

        using var media = new Media(_libVlc, new Uri(filePath));
        if (!MediaPlayer.Play(media))
        {
            Finish(() =>
            {
                _logger.LogWarning("LibVLC refused to play {File}", Path.GetFileName(filePath));
                tcs.TrySetException(new InvalidOperationException($"LibVLC refused to play '{filePath}'"));
            });
        }

        return tcs.Task;
    }

    public async Task PlayBytesAsync(byte[] audioData, string fileExtension = "wav", CancellationToken ct = default)
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"tts_{Guid.NewGuid():N}.{fileExtension}");
        await File.WriteAllBytesAsync(tmp, audioData, ct).ConfigureAwait(false);

        _logger.LogDebug("Playing {Bytes} bytes of {Format} audio", audioData.Length, fileExtension);

        await PlayFileAsync(tmp, ct).ConfigureAwait(false);
    }

    public void Stop() => MediaPlayer?.Stop();

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* intentionally swallowed */ }
    }

    public void Dispose()
    {
        MediaPlayer?.Stop();
        MediaPlayer?.Dispose();
        _libVlc?.Dispose();  // was missing null-check
        _initLock.Dispose();
    }
}
