using Peek.Core.Settings;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using System.Text;

namespace Peek.Core.Services.Speech;

/// <summary>
/// Records every announcement spoken through AccessibilitySpeechService into a running
/// transcript, for reading-impaired users who want to review or copy what was just read
/// aloud rather than relying on memory. Toggleable via AnnouncementHistorySettings.Enabled
/// and length-capped - oldest text scrolls off the front once MaxCharacters is exceeded,
/// the same idea as a terminal scrollback buffer.
/// </summary>
public sealed partial class AnnouncementHistoryService : ReactiveObject
{
    private readonly ISettingsService _settings;
    private readonly StringBuilder _buffer = new();

    [Reactive]
    private string _transcript = "";

    public AnnouncementHistoryService(ISettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>Appends one full announcement's text as its own line - called once per AnnounceAsync/AnnounceTextAsync call, not once per TTS segment, so the transcript reads as whole utterances.</summary>
    public void Append(string text)
    {
        if (!_settings.Current.AnnouncementHistory.Enabled || string.IsNullOrWhiteSpace(text)) return;

        if (_buffer.Length > 0) _buffer.Append('\n');
        _buffer.Append(text);

        var max = Math.Max(1000, _settings.Current.AnnouncementHistory.MaxCharacters);
        if (_buffer.Length > max)
            _buffer.Remove(0, _buffer.Length - max);

        Transcript = _buffer.ToString();
    }

    public void Clear()
    {
        _buffer.Clear();
        Transcript = "";
    }
}
