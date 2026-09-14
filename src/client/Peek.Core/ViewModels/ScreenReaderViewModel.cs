using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Peek.Core.Abstractions;
using Peek.Core.Services;
using Peek.Core.Services.Llm;
using Peek.Core.Services.Speech;
using Peek.Core.Settings;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;

namespace Peek.Core.ViewModels;

public partial class ScreenReaderViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger _logger;
    private readonly ElementTracker _elementTracker;
    private readonly IElementDescriptionService _describer;
    private readonly IClipboardService _clipboard;
    private readonly IDisposable _historyEnabledSubscription;
    private bool _disposed;

    [Reactive]
    private bool _isDescribing;

    [Reactive]
    private bool _historyEnabled;

    public ScreenReaderViewModel(IServiceProvider serviceProvider, ILogger<MainViewModel> logger)
    {
        _logger = logger;
        _elementTracker = serviceProvider.GetRequiredService<ElementTracker>();
        _describer = serviceProvider.GetRequiredService<IElementDescriptionService>();
        _clipboard = serviceProvider.GetRequiredService<IClipboardService>();
        History = serviceProvider.GetRequiredService<AnnouncementHistoryService>();
        ScreenAnalysis = serviceProvider.GetRequiredService<ScreenAnalysisService>();

        var settingsService = serviceProvider.GetRequiredService<ISettingsService>();
        _historyEnabled = settingsService.Current.AnnouncementHistory.Enabled;
        // Live-reacts to the Settings page's toggle (ScreenReaderView is region-cached, see
        // PreferCache - without this subscription the panel wouldn't show/hide until the
        // whole view was reconstructed, i.e. an app restart).
        _historyEnabledSubscription = settingsService.Changes.Subscribe(
            s => HistoryEnabled = s.AnnouncementHistory.Enabled);

        var disposeService = serviceProvider.GetRequiredService<IDisposeService>();
        disposeService.Register(this);
    }

    public ElementTracker ElementTracker => _elementTracker;

    public AnnouncementHistoryService History { get; }

    public ScreenAnalysisService ScreenAnalysis { get; }

    [ReactiveCommand]
    public async Task Describe()
    {
        var element = _elementTracker.CurrentElement;
        if (element is null || IsDescribing) return;

        IsDescribing = true;
        try
        {
            await _describer.DescribeAsync(element);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Describe command failed");
        }
        finally
        {
            IsDescribing = false;
        }
    }

    [ReactiveCommand]
    public Task CopyHistory() => _clipboard.SetTextAsync(History.Transcript);

    [ReactiveCommand]
    public void ClearHistory() => History.Clear();

    [ReactiveCommand]
    public Task AnalyzeScreen() => ScreenAnalysis.AnalyzeScreenAsync();

    [ReactiveCommand]
    public void StopAnalysis() => ScreenAnalysis.Cancel();

    public void Dispose()
    {
        // Disposed twice on ordinary app exit: the region navigation library disposes the
        // hosted view/viewmodel when the shell clears its region, and this also self-registers
        // with IDisposeService (see ctor) for MainViewModel's own teardown pass right after -
        // see ElementTracker.Dispose for the same guard and the crash this fixes.
        if (_disposed) return;
        _disposed = true;

        _historyEnabledSubscription.Dispose();
        _elementTracker.Dispose();
    }
}
