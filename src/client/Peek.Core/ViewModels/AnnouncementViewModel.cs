using System.Collections.Specialized;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Peek.Core.Abstractions;
using Peek.Core.Models;
using Peek.Core.Services;
using Peek.Core.Services.Speech;
using Peek.Core.Settings;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;

namespace Peek.Core.ViewModels;

/// <summary>View model for the persistent bottom announcement strip.</summary>
public partial class AnnouncementViewModel : ViewModelBase, IDisposable
{
    private readonly IClipboardService _clipboard;
    private readonly IAccessibilitySpeechService _speech;
    private readonly WindowEnumerator _windowEnumerator;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<AnnouncementViewModel> _logger;
    private readonly System.Timers.Timer _pollTimer;
    private readonly SynchronizationContext? _uiContext;
    private readonly IDisposable _settingsSubscription;
    private readonly NotifyCollectionChangedEventHandler _entriesChangedHandler;
    private bool _disposed;

    [Reactive] private bool _isSpeaking;
    [Reactive] private bool _historyEnabled;
    [Reactive] private bool _isExpanded;
    [Reactive] private AnnouncementEntry? _selectedEntry;

    public AnnouncementViewModel(IServiceProvider services, ILogger<AnnouncementViewModel> logger)
    {
        History = services.GetRequiredService<AnnouncementHistoryService>();
        _clipboard = services.GetRequiredService<IClipboardService>();
        _speech = services.GetRequiredService<IAccessibilitySpeechService>();
        _windowEnumerator = services.GetRequiredService<WindowEnumerator>();
        _settingsService = services.GetRequiredService<ISettingsService>();
        _logger = logger;
        _uiContext = SynchronizationContext.Current;

        _historyEnabled = _settingsService.Current.AnnouncementHistory.Enabled;
        _settingsSubscription = _settingsService.Changes.Subscribe(s =>
            _uiContext?.Post(_ => HistoryEnabled = s.AnnouncementHistory.Enabled, null));

        // Entries is only ever mutated on this same UI thread (see AnnouncementHistoryService's
        // own doc comment), so this needs no marshaling of its own - it just has to keep
        // LatestAnnouncement's binding refreshed, since a computed property raises no
        // notification on its own when the thing it reads from changes.
        _entriesChangedHandler = (_, _) => this.RaisePropertyChanged(nameof(LatestAnnouncement));
        History.Entries.CollectionChanged += _entriesChangedHandler;

        _pollTimer = new System.Timers.Timer(80) { AutoReset = true };
        _pollTimer.Elapsed += (_, _) =>
        {
            if (_uiContext is { } context)
                context.Post(_ => IsSpeaking = _speech.IsSpeaking, null);
            else
                IsSpeaking = _speech.IsSpeaking;
        };
        _pollTimer.Start();
    }

    public AnnouncementHistoryService History { get; }

    public string LatestAnnouncement =>
        History.Entries.Count > 0
            ? History.Entries[^1].Text
            : SpeechStrings.Get("ScreenReader_HistoryEmpty", SpeechCulture);

    private CultureInfo SpeechCulture => SpeechStrings.ResolveCulture(_settingsService.Current.Localization);

    [ReactiveCommand]
    public void ToggleExpanded() => IsExpanded = !IsExpanded;

    [ReactiveCommand]
    public Task CopyHistory() => _clipboard.SetTextAsync(History.Transcript);

    [ReactiveCommand]
    public void ClearHistory() => History.Clear();

    /// <summary>
    /// Re-speaks the selected history entry and, if it was about a native window, tries to
    /// bring that window forward too - a screen-reader user who missed or wants to act on
    /// something Peek said earlier doesn't have to remember what it was or go find the window
    /// themselves. Deliberately doesn't add a new entry for the replay itself (recordInHistory:
    /// false) - re-speaking history shouldn't grow the history it's replaying from.
    /// </summary>
    [ReactiveCommand]
    public async Task ReplaySelected()
    {
        if (SelectedEntry is not { } entry) return;

        try
        {
            await _speech.AnnounceTextAsync(
                entry.Text, SpeechPriority.UserRequested,
                sourceWindowHandle: entry.SourceWindowHandle, recordInHistory: false).ConfigureAwait(false);

            if (entry.SourceWindowHandle == default) return;

            // Silent (no extra announcement) when the window's gone - the absence of a
            // confirmation already tells a sighted user nothing happened, and re-reading the
            // stale text a second time with a caveat attached would be noisier than useful.
            var window = _windowEnumerator.SnapshotSingle(entry.SourceWindowHandle);
            if (window is null) return;

            _windowEnumerator.EnsureWindowForeground(entry.SourceWindowHandle);
            await _speech.AnnounceTextAsync(
                SpeechStrings.Format("Speech_AnnouncementHistory_SwitchedToFormat", SpeechCulture, window.Title),
                SpeechPriority.UserRequested, recordInHistory: false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to replay a history entry");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollTimer.Dispose();
        _settingsSubscription.Dispose();
        History.Entries.CollectionChanged -= _entriesChangedHandler;
    }
}
