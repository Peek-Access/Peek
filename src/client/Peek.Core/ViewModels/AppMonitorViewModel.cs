using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Peek.Worker.Contracts.SystemMonitor;
using Peek.Core.Abstractions;
using Peek.Core.Services;
using Peek.Core.Services.Speech;
using Peek.Core.Settings;
using Peek.Ipc.Worker;
using ReactiveUI.SourceGenerators;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Peek.Core.ViewModels;

/// <summary>
/// Lists the OS's installed apps (via the worker's registry read - see
/// ISystemMonitorService) with sortable Name/Version/Publisher/Disk-usage columns.
/// Selecting a row announces it; launching (by mouse, Enter, or the configurable
/// AppMonitorLaunch shortcut) announces success or failure. One of the four monitor
/// views - see MainViewModel/DockShellViewModel for how it's hosted.
/// </summary>
public partial class AppMonitorViewModel : MonitorViewModelBase
{
    private readonly WorkerConnection _workerConnection;
    private readonly IAccessibilitySpeechService _speechService;
    private readonly ILogger<AppMonitorViewModel> _logger;
    private readonly ISettingsService _settingsService;

    /// <summary>Spoken announcements follow the TTS language, not the UI language - see SpeechStrings.</summary>
    private CultureInfo SpeechCulture => SpeechStrings.ResolveCulture(_settingsService.Current.Localization);

    public string LaunchShortcutGesture { get; }

    public ObservableCollection<InstalledAppDto> Apps { get; } = [];

    [Reactive]
    private InstalledAppDto? _selectedApp;

    public AppMonitorViewModel(IServiceProvider serviceProvider, ILogger<AppMonitorViewModel> logger)
    {
        _workerConnection = serviceProvider.GetRequiredService<WorkerConnection>();
        _speechService = serviceProvider.GetRequiredService<IAccessibilitySpeechService>();
        _logger = logger;

        _settingsService = serviceProvider.GetRequiredService<ISettingsService>();
        LaunchShortcutGesture = _settingsService.Current.Keyboard.Shortcuts.GetValueOrDefault("AppMonitorLaunch", "Enter");

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SelectedApp) && SelectedApp is { } app)
                _ = AnnounceSelectionAsync(app);
        };

        LoadWhenWorkerReady(_workerConnection, FetchAppsAsync, _logger, "Failed to load installed apps");
    }

    [ReactiveCommand]
    private async Task Refresh() => await LoadAsync();

    private Task LoadAsync() => RunLoadAsync(FetchAppsAsync, _logger, "Failed to load installed apps");

    private async Task FetchAppsAsync()
    {
        var apps = await _workerConnection.Client.SystemMonitor.EnumerateAppsAsync();
        _logger.LogDebug("Loaded {Count} installed apps", apps.Count);
        Apps.Clear();
        foreach (var app in apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
            Apps.Add(app);
    }

    public async Task LaunchSelectedAsync()
    {
        if (SelectedApp is not { } app || !app.CanLaunch) return;

        try
        {
            var result = await _workerConnection.Client.SystemMonitor.LaunchAppAsync(app.Key);
            var text = result.Success
                ? InstalledAppAnnouncementFormatter.FormatLaunchSucceeded(app.Name, SpeechCulture)
                : InstalledAppAnnouncementFormatter.FormatLaunchFailed(app.Name, result.Error, SpeechCulture);
            await _speechService.AnnounceTextAsync(text, SpeechPriority.UserRequested);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to launch {Name}", app.Name);
            await _speechService.AnnounceTextAsync(InstalledAppAnnouncementFormatter.FormatLaunchFailed(app.Name, ex.Message, SpeechCulture), SpeechPriority.UserRequested);
        }
    }

    [ReactiveCommand]
    private Task Launch() => LaunchSelectedAsync();

    private async Task AnnounceSelectionAsync(InstalledAppDto app)
    {
        try
        {
            await _speechService.AnnounceTextAsync(InstalledAppAnnouncementFormatter.FormatSelection(app.Name, app.Version, app.Publisher, SpeechCulture), SpeechPriority.UserRequested);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to announce selected app {Name}", app.Name);
        }
    }
}
