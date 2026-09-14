namespace Peek.Core.Services;

/// <summary>
/// Plays a short "tick" through <see cref="AudioPlayer"/> - same cross-platform-capable
/// (LibVLC-backed) path <see cref="NotificationSoundPlayer"/> uses - whenever the dock
/// strip cycles between monitors (DockShellViewModel.NavigateByOffsetAsync).
/// </summary>
public sealed class MonitorSwitchSoundPlayer
{
    private const double DurationSeconds = 0.08;
    private const double ToneHz = 1046.5; // C6 - short and crisp, distinct from the lower TTS/chime register

    private static readonly byte[] ToneBytes = ToneSynthesizer.GenerateTone(ToneHz, DurationSeconds);

    private readonly AudioPlayer _audioPlayer;

    public MonitorSwitchSoundPlayer(AudioPlayer audioPlayer)
    {
        _audioPlayer = audioPlayer;
    }

    public async Task PlayAsync()
    {
        await _audioPlayer.VlcInitializeAsync().ConfigureAwait(false);
        await _audioPlayer.PlayBytesAsync(ToneBytes, "wav").ConfigureAwait(false);
    }
}
