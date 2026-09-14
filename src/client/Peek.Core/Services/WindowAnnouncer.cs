using Microsoft.Extensions.Logging;
using Peek.Core.Services.Speech;
using Peek.Core.Settings;

namespace Peek.Core.Services;

/// <summary>
/// Optional accessibility layer that announces window open/close/focus-change events
/// via TTS, each independently toggle-able plus a master on/off switch (see
/// <see cref="WindowAnnouncementSettings"/>) - entirely opt-in and off by default.
/// </summary>
/// <remarks>
/// The underlying hooks (<see cref="WindowTracker"/>/<see cref="WindowLifecycleTracker"/>)
/// keep running regardless of the settings - they're cheap OS event subscriptions - and
/// every setting is re-read live on each event rather than cached at construction, so
/// flipping any toggle in Settings takes effect on the very next window event with no
/// restart needed.
/// </remarks>
public sealed class WindowAnnouncer : IDisposable
{
    private readonly WindowTracker _windowTracker;
    private readonly WindowLifecycleTracker _lifecycleTracker;
    private readonly WindowEnumerator _windowEnumerator;
    private readonly IAccessibilitySpeechService _speechService;
    private readonly NotificationSoundPlayer _notificationSound;
    private readonly ISettingsService _settings;
    private readonly ILogger<WindowAnnouncer> _logger;
    private int _disposed;

    public WindowAnnouncer(
        WindowTracker windowTracker,
        WindowLifecycleTracker lifecycleTracker,
        WindowEnumerator windowEnumerator,
        IAccessibilitySpeechService speechService,
        NotificationSoundPlayer notificationSound,
        ISettingsService settings,
        ILogger<WindowAnnouncer> logger)
    {
        _windowTracker = windowTracker;
        _lifecycleTracker = lifecycleTracker;
        _windowEnumerator = windowEnumerator;
        _speechService = speechService;
        _notificationSound = notificationSound;
        _settings = settings;
        _logger = logger;

        _windowTracker.ForegroundWindowChanged += OnForegroundWindowChanged;
        _lifecycleTracker.WindowLifecycleChanged += OnWindowLifecycleChanged;
    }

    private void OnForegroundWindowChanged(object? sender, ForegroundWindowChangedArgs e)
    {
        var s = _settings.Current.WindowAnnouncement;
        if (!s.Enabled || !s.AnnounceFocusChanged) return;

        _ = AnnounceAsync(WindowAnnouncementKind.FocusChanged, e.WindowTitle, e.ProcessId);
    }

    private void OnWindowLifecycleChanged(object? sender, WindowLifecycleChangedArgs e)
    {
        var s = _settings.Current.WindowAnnouncement;
        if (!s.Enabled) return;

        var kind = e.Kind == WindowLifecycleChangeKind.Opened
            ? WindowAnnouncementKind.Opened
            : WindowAnnouncementKind.Closed;

        if (kind == WindowAnnouncementKind.Opened && !s.AnnounceWindowOpened) return;
        if (kind == WindowAnnouncementKind.Closed && !s.AnnounceWindowClosed) return;

        _ = AnnounceAsync(kind, e.WindowTitle, e.ProcessId);
    }

    private async Task AnnounceAsync(WindowAnnouncementKind kind, string windowTitle, uint processId)
    {
        try
        {
            var subject = _settings.Current.WindowAnnouncement.Subject == WindowAnnouncementSubject.ProcessName
                ? _windowEnumerator.GetProcessName(processId)
                : windowTitle;

            var text = WindowAnnouncementFormatter.Format(
                kind, subject, SpeechStrings.ResolveCulture(_settings.Current.Localization));
            if (text is null) return;

            // Sequential, not fire-and-forget-in-parallel: PlayAsync only returns once
            // the chime has finished, so AccessibilitySpeechService's own AudioPlayer.Stop()
            // right before it plays the TTS clip can never cut the chime off mid-play.
            await _notificationSound.PlayAsync().ConfigureAwait(false);
            await _speechService.AnnounceTextAsync(text, SpeechPriority.Ambient).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Window-change announcement failed ({Kind})", kind);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _windowTracker.ForegroundWindowChanged -= OnForegroundWindowChanged;
        _lifecycleTracker.WindowLifecycleChanged -= OnWindowLifecycleChanged;
    }
}
