using Microsoft.Extensions.DependencyInjection;
using Peek.Core.Abstractions;
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
    private readonly System.Timers.Timer _pollTimer;
    private readonly SynchronizationContext? _uiContext;
    private readonly IDisposable _settingsSubscription;
    private bool _disposed;

    [Reactive] private bool _isSpeaking;
    [Reactive] private bool _historyEnabled;
    [Reactive] private bool _isExpanded;
    [Reactive] private string _latestAnnouncement = "Nothing announced yet";

    public AnnouncementViewModel(IServiceProvider services)
    {
        History = services.GetRequiredService<AnnouncementHistoryService>();
        _clipboard = services.GetRequiredService<IClipboardService>();
        _speech = services.GetRequiredService<IAccessibilitySpeechService>();
        _uiContext = SynchronizationContext.Current;
        var settings = services.GetRequiredService<ISettingsService>();
        _historyEnabled = settings.Current.AnnouncementHistory.Enabled;
        _settingsSubscription = settings.Changes.Subscribe(s =>
            _uiContext?.Post(_ => HistoryEnabled = s.AnnouncementHistory.Enabled, null));
        History.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AnnouncementHistoryService.Transcript))
            {
                var lines = History.Transcript.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var latest = lines.Length == 0 ? "Nothing announced yet" : lines[^1];
                _uiContext?.Post(_ => LatestAnnouncement = latest, null);
            }
        };
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

    [ReactiveCommand]
    public void ToggleExpanded() => IsExpanded = !IsExpanded;

    [ReactiveCommand]
    public Task CopyHistory() => _clipboard.SetTextAsync(History.Transcript);

    [ReactiveCommand]
    public void ClearHistory() => History.Clear();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollTimer.Dispose();
        _settingsSubscription.Dispose();
    }
}
