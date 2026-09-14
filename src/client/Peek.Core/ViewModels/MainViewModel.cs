using AsyncNavigation.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Peek.Core.Abstractions;
using Peek.Core.Services;
using Peek.Core.Services.Speech;
using Peek.Core.Settings;
using Peek.Ipc.Worker;
using Peek.Ipc.Connection;
using ReactiveUI;
using ReactiveUI.Primitives.Disposables;
using ReactiveUI.SourceGenerators;
using ReactiveUI.Primitives;

namespace Peek.Core.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly MultipleDisposable _disposables = [];
    private readonly ILogger _logger;
    private readonly IRegionManager _regionManager;
    private readonly IViewManager _viewManager;
    private readonly IDisposeService _disposeService;
    private readonly IMouseTracker _mouseTracker;
    private readonly IDialogService _dialogService;
    private readonly WorkerConnection _workerConnection;
    private readonly IAccessibilitySpeechService _speechService;
    private readonly ISettingsService _settingsService;

    [Reactive]
    private string? _currentViewName;
    [Reactive]
    private ConnectionState _workerState;

    public string AppVersion => AppInfo.ShortVersion;

    public MainViewModel(IServiceProvider serviceProvider, ILogger<MainViewModel> logger)
    {
        _logger = logger;
        _regionManager = serviceProvider.GetRequiredService<IRegionManager>();
        _viewManager = serviceProvider.GetRequiredService<IViewManager>();
        _mouseTracker = serviceProvider.GetRequiredService<IMouseTracker>();
        _dialogService = serviceProvider.GetRequiredService<IDialogService>();
        _workerConnection = serviceProvider.GetRequiredService<WorkerConnection>();
        _speechService = serviceProvider.GetRequiredService<IAccessibilitySpeechService>();
        _settingsService = serviceProvider.GetRequiredService<ISettingsService>();

        _disposeService = serviceProvider.GetRequiredService<IDisposeService>();
        _ = serviceProvider.GetRequiredService<AudioPlayer>().VlcInitializeAsync();

        // Pays the window-enumeration path's one-time JIT cost in the background, so the first
        // switch to the window monitor doesn't - see WindowEnumerator.WarmUpAsync. Well after
        // startup on purpose.
        _ = serviceProvider.GetRequiredService<WindowEnumerator>()
            .WarmUpAsync(TimeSpan.FromSeconds(8));

        _logger.LogInformation("[Init] MainViewModel ctor finished");

        if (_regionManager.TryGetRegion("MainRegion", out var region))
        {
            region.Navigated += (s, e) =>
            {
                CurrentViewName = e.Context.ViewName;
            };
        }

        // Real connection status for the sidebar footer: a hardcoded "always green" dot would
        // lie to the user the moment the worker actually dropped.
        _workerConnection.State
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(s => WorkerState = s)
            .DisposeWith(_disposables);

        _ = InitializeAsync().ContinueWith(
            t => _logger.LogCritical(t.Exception, "MainViewModel initialization failed"),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _disposables?.Dispose();
        _viewManager.Clear();
        _disposeService.DisposeAll();
    }
    [ReactiveCommand]
    public async Task Navigation(string viewName)
    {
        await _regionManager.RequestNavigateAsync("MainRegion", viewName);
    }

    /// <summary>Called by MainWindow's title-bar pin toggle - the window's Topmost property
    /// itself lives on the Window, not here, so this only speaks the resulting state.</summary>
    public void AnnouncePinStateChanged(bool isPinned)
    {
        var culture = SpeechStrings.ResolveCulture(_settingsService.Current.Localization);
        var text = SpeechStrings.Get(isPinned ? "Speech_MainWindow_Pinned" : "Speech_MainWindow_Unpinned", culture);
        _ = _speechService.AnnounceTextAsync(text, SpeechPriority.UserRequested);
    }
    public async Task InitializeAsync()
    {

        var ret = await _regionManager.RequestNavigateAsync("MainRegion", "ScreenReaderView");
        _mouseTracker.SelectedStream
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ => 
            {
                
            });
    }
}
