namespace Peek.Core.Settings;

/// <summary>
/// Controls the running transcript of everything AccessibilitySpeechService has spoken
/// (see AnnouncementHistoryService) - shown in ScreenReaderView so a reading-impaired user
/// can review or copy what was just read aloud instead of relying on memory.
/// </summary>
public sealed class AnnouncementHistorySettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Transcript length cap in characters - oldest text scrolls off the front once exceeded, like a terminal scrollback buffer.</summary>
    public int MaxCharacters { get; set; } = 20_000;

    public void Validate()
    {
        if (MaxCharacters < 1000) MaxCharacters = 1000;
    }
}
