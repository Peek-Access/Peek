using Microsoft.Extensions.Logging;
using Peek.Worker.Contracts.Automation;
using Peek.Core.Settings;
using Peek.Ipc.Worker;

namespace Peek.Core.Services.Speech;

public sealed class AccessibilitySpeechService : IAccessibilitySpeechService, IDisposable
{
    private readonly WorkerConnection _workerConnection;
    private readonly AudioPlayer _audioPlayer;
    private readonly ISpeechPolicy _speechPolicy;
    private readonly ISettingsService _settings;
    private readonly AnnouncementHistoryService _history;
    private readonly ILogger<AccessibilitySpeechService> _logger;
    private CancellationTokenSource _ttsCts = new();

    /// <summary>
    /// Leases handed out by <see cref="BeginExclusive"/>. Held in a list rather than a counter
    /// because <see cref="StopAsync"/> has to reach back into each owner to cancel it.
    /// </summary>
    private readonly List<ExclusiveLease> _leases = [];
    private readonly object _leaseGate = new();

    /// <summary>1 while a single user-requested utterance is being synthesized or played.</summary>
    private int _userUtteranceInFlight;

    public AccessibilitySpeechService(
        WorkerConnection workerConnection,
        AudioPlayer audioPlayer,
        ISpeechPolicy speechPolicy,
        ISettingsService settings,
        AnnouncementHistoryService history,
        ILogger<AccessibilitySpeechService> logger)
    {
        _workerConnection = workerConnection;
        _audioPlayer = audioPlayer;
        _speechPolicy = speechPolicy;
        _settings = settings;
        _history = history;
        _logger = logger;
    }

    public bool IsChannelReserved
    {
        get
        {
            if (Volatile.Read(ref _userUtteranceInFlight) > 0) return true;
            lock (_leaseGate) return _leases.Count > 0;
        }
    }

    public Task AnnounceAsync(SemanticElement element, SpeechPriority priority, CancellationToken ct = default)
    {
        // Formatting is skipped entirely for an ambient announcement that is going to be
        // dropped - this runs on every hover, and the speech policy walk is not free.
        if (priority == SpeechPriority.Ambient && IsChannelReserved)
        {
            _logger.LogDebug("Dropped ambient announcement for {Name} - the speech channel is reserved", element.Name);
            return Task.CompletedTask;
        }

        var content = _speechPolicy.Describe(
            element,
            _settings.Current.Speech.Verbosity,
            SpeechStrings.ResolveCulture(_settings.Current.Localization));

        return SpeakAsync(content.Text, element.Name, priority, ct);
    }

    public Task AnnounceTextAsync(string text, SpeechPriority priority, CancellationToken ct = default) =>
        SpeakAsync(text, text, priority, ct);

    public IDisposable BeginExclusive(string reason, Action? onStopRequested = null)
    {
        var lease = new ExclusiveLease(this, reason, onStopRequested);

        lock (_leaseGate)
            _leases.Add(lease);

        _logger.LogDebug("Speech channel reserved for {Reason} - ambient announcements will be dropped", reason);
        return lease;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        // Tell any exclusive owner to give up first, before silencing anything: an AI
        // narration that is mid-stream would otherwise just start speaking its next sentence
        // a moment later, and the user would have to keep pressing the key to win.
        ExclusiveLease[] leases;
        lock (_leaseGate)
            leases = [.. _leases];

        foreach (var lease in leases)
            lease.RequestStop();

        // Cancel the in-flight synthesis first so a segment that's still being generated
        // can't start playing right after the player is stopped, then stop both ends:
        // local playback (what the user is hearing now) and the worker's own piper process
        // (what it would hand back next).
        await _ttsCts.CancelAsync().ConfigureAwait(false);
        _audioPlayer.Stop();

        try
        {
            await _workerConnection.Client.Tts.StopAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Worst case the worker keeps synthesizing a clip nobody will ever play - the
            // user already got silence, which is what they asked for.
            _logger.LogDebug(ex, "Worker-side TTS stop failed");
        }
    }

    private async Task SpeakAsync(string text, string logLabel, SpeechPriority priority, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        if (priority == SpeechPriority.Ambient && IsChannelReserved)
        {
            // Dropped, not queued, and deliberately not added to the history either - it was
            // never spoken, and by the time the channel frees up it describes the past.
            _logger.LogDebug("Dropped ambient announcement - the speech channel is reserved: {Label}", logLabel);
            return;
        }

        _history.Append(text);

        var isUserRequested = priority == SpeechPriority.UserRequested;
        if (isUserRequested) Interlocked.Increment(ref _userUtteranceInFlight);

        var oldCts = _ttsCts;
        _ttsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await oldCts.CancelAsync();
        oldCts.Dispose();

        try
        {
            var speech = _settings.Current.Speech;
            var localization = _settings.Current.Localization;
            var primaryLanguage = SpeechLanguageDetector.ToLanguageCode(
                string.IsNullOrWhiteSpace(localization.TtsLanguage) ? localization.UiLanguage : localization.TtsLanguage);
            var segments = SpeechLanguageDetector.Segment(text, primaryLanguage);

            _audioPlayer.Stop();

            var interrupt = true;
            foreach (var segment in segments)
            {
                var voiceId = speech.VoiceIdByLanguage.GetValueOrDefault(segment.Language);
                var result = await _workerConnection.Client.Tts
                    .SpeakAsync(segment.Text, voiceId, speech.Rate, interrupt: interrupt, ct: _ttsCts.Token)
                    .ConfigureAwait(false);
                interrupt = false;

                _logger.LogDebug("Speaking ({Priority}, {Lang}, {Bytes} bytes): {Text}",
                    priority, segment.Language, result.AudioData.Length, segment.Text);

                // Pass the token through: without it an interrupted clip left this await
                // pending on a player that had already moved on to the next announcement.
                await _audioPlayer
                    .PlayBytesAsync(result.AudioData, result.Format, _ttsCts.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer announcement - expected during fast mouse movement.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Announce failed for: {Label}", logLabel);
        }
        finally
        {
            if (isUserRequested) Interlocked.Decrement(ref _userUtteranceInFlight);
        }
    }

    private void Release(ExclusiveLease lease)
    {
        bool removed;
        lock (_leaseGate)
            removed = _leases.Remove(lease);

        if (removed)
            _logger.LogDebug("Speech channel released by {Reason}", lease.Reason);
    }

    public void Dispose()
    {
        _ttsCts.Cancel();
        _ttsCts.Dispose();
    }

    private sealed class ExclusiveLease(
        AccessibilitySpeechService owner,
        string reason,
        Action? onStopRequested) : IDisposable
    {
        private int _disposed;

        public string Reason { get; } = reason;

        public void RequestStop()
        {
            try
            {
                onStopRequested?.Invoke();
            }
            catch (Exception ex)
            {
                owner._logger.LogWarning(ex, "A speech-channel owner failed to stop on request ({Reason})", Reason);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            owner.Release(this);
        }
    }
}
