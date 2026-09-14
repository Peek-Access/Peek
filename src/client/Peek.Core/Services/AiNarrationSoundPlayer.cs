namespace Peek.Core.Services;

/// <summary>
/// Three short audible cues that bracket an AI screen/window analysis (see
/// ScreenAnalysisService): the user has no visual progress indicator to rely on, so each
/// phase transition - request sent, first words arriving, finished - gets its own distinct
/// tone instead of leaving a reading-impaired user guessing whether anything is happening.
/// </summary>
public sealed class AiNarrationSoundPlayer
{
    private static readonly byte[] ThinkingTone = ToneSynthesizer.GenerateTone(392.0, 0.09, 0.3);   // G4 - "request sent, waiting"
    private static readonly byte[] SpeakingStartTone = ToneSynthesizer.GenerateTone(659.3, 0.07, 0.3); // E5 - "first words arriving"
    private static readonly byte[] DoneTone = ToneSynthesizer.GenerateSequence((523.3, 0.07), (784.0, 0.09)); // C5->G5 - "finished"

    private readonly AudioPlayer _audioPlayer;

    public AiNarrationSoundPlayer(AudioPlayer audioPlayer)
    {
        _audioPlayer = audioPlayer;
    }

    public Task PlayThinkingAsync() => PlayAsync(ThinkingTone);

    public Task PlaySpeakingStartAsync() => PlayAsync(SpeakingStartTone);

    public Task PlayDoneAsync() => PlayAsync(DoneTone);

    private async Task PlayAsync(byte[] tone)
    {
        await _audioPlayer.VlcInitializeAsync().ConfigureAwait(false);
        await _audioPlayer.PlayBytesAsync(tone, "wav").ConfigureAwait(false);
    }
}
