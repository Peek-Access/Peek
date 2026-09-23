using System.Collections.ObjectModel;
using Peek.Core.Models;
using Peek.Core.Settings;

namespace Peek.Core.Services.Speech;

/// <summary>
/// Records every announcement spoken through AccessibilitySpeechService as a structured
/// <see cref="AnnouncementEntry"/>, for reading-impaired users who want to review, copy, or
/// replay what was just read aloud rather than relying on memory. Toggleable via
/// AnnouncementHistorySettings.Enabled and length-capped - oldest entries drop off the front
/// once MaxCharacters is exceeded, the same idea as a terminal scrollback buffer.
/// </summary>
/// <remarks>
/// <see cref="Entries"/> is bound directly to Announcement History's ListBox
/// (AnnouncementView/DockAnnouncementView), so every mutation has to land on the UI thread -
/// <see cref="Append"/> is called from wherever AccessibilitySpeechService.SpeakAsync happens
/// to run, which is very often a background thread. The constructor captures whatever
/// SynchronizationContext is current, exactly like AnnouncementViewModel already does for its
/// own state - both rely on this service being constructed during App.axaml.cs's synchronous,
/// UI-thread startup sequence, before anything could resolve it from elsewhere.
/// </remarks>
public sealed class AnnouncementHistoryService
{
    private readonly ISettingsService _settings;
    private readonly SynchronizationContext? _uiContext;

    /// <summary>Running total of Entries[*].Text.Length, maintained incrementally rather than
    /// re-summed on every Append - history can grow into the thousands of entries over a long
    /// session, and re-scanning all of it on every single announcement would be needless,
    /// repeated UI-thread work for something a running counter answers in O(1).</summary>
    private int _totalCharacters;

    public AnnouncementHistoryService(ISettingsService settings)
    {
        _settings = settings;
        _uiContext = SynchronizationContext.Current;
    }

    /// <summary>Every announcement spoken, oldest first.</summary>
    public ObservableCollection<AnnouncementEntry> Entries { get; } = [];

    /// <summary>The whole history as one newline-joined block, for the Copy button - computed on demand rather than cached, since it's only ever read right before it's copied.</summary>
    public string Transcript => string.Join('\n', Entries.Select(e => e.Text));

    /// <summary>Records one full announcement - called once per AnnounceAsync/AnnounceTextAsync call, not once per TTS segment, so the history reads as whole utterances.</summary>
    public void Append(string text, nint sourceWindowHandle = default)
    {
        if (!_settings.Current.AnnouncementHistory.Enabled || string.IsNullOrWhiteSpace(text)) return;

        var entry = new AnnouncementEntry(text, DateTimeOffset.Now, sourceWindowHandle);
        RunOnUiThread(() =>
        {
            Entries.Add(entry);
            _totalCharacters += entry.Text.Length;
            TrimToCapacity();
        });
    }

    public void Clear() => RunOnUiThread(() =>
    {
        Entries.Clear();
        _totalCharacters = 0;
    });

    /// <summary>Drops the oldest entries once the total character count exceeds the cap - never the most recently added one, even if that single entry alone exceeds it, so appending never makes the history it just grew appear empty.</summary>
    private void TrimToCapacity()
    {
        var max = Math.Max(1000, _settings.Current.AnnouncementHistory.MaxCharacters);

        while (_totalCharacters > max && Entries.Count > 1)
        {
            _totalCharacters -= Entries[0].Text.Length;
            Entries.RemoveAt(0);
        }
    }

    private void RunOnUiThread(Action action)
    {
        if (_uiContext is null) action();
        else _uiContext.Post(_ => action(), null);
    }
}
