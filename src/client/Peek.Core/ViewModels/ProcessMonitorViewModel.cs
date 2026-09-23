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
/// Lists the OS's running processes (via the worker - see ISystemMonitorService) with
/// sortable Name/PID/Memory/Description columns. System (session 0) processes are hidden
/// by default (ProcessMonitorSettings.ShowSystemProcesses). Selecting a row announces it;
/// ending a process (by right-click, or the configurable ProcessMonitorKill shortcut) is
/// refused server-side for system processes regardless of what's shown client-side. One of
/// the four monitor views - see MainViewModel/DockShellViewModel for how it's hosted.
/// </summary>
public partial class ProcessMonitorViewModel : MonitorViewModelBase
{
    private readonly WorkerConnection _workerConnection;
    private readonly IAccessibilitySpeechService _speechService;
    private readonly ISettingsService _settingsService;
    private readonly WindowEnumerator _windowEnumerator;

    /// <summary>Spoken announcements follow the TTS language, not the UI language - see SpeechStrings.</summary>
    private CultureInfo SpeechCulture => SpeechStrings.ResolveCulture(_settingsService.Current.Localization);
    private readonly ILogger<ProcessMonitorViewModel> _logger;

    public string KillShortcutGesture { get; }
    public string OpenFolderShortcutGesture { get; }

    public ObservableCollection<ProcessDto> Processes { get; } = [];

    [Reactive]
    private ProcessDto? _selectedProcess;
    [Reactive]
    private bool _showSystemProcesses;

    public ProcessMonitorViewModel(IServiceProvider serviceProvider, ILogger<ProcessMonitorViewModel> logger)
    {
        _workerConnection = serviceProvider.GetRequiredService<WorkerConnection>();
        _speechService = serviceProvider.GetRequiredService<IAccessibilitySpeechService>();
        _settingsService = serviceProvider.GetRequiredService<ISettingsService>();
        _windowEnumerator = serviceProvider.GetRequiredService<WindowEnumerator>();
        _logger = logger;

        var settings = _settingsService.Current;
        KillShortcutGesture = settings.Keyboard.Shortcuts.GetValueOrDefault("ProcessMonitorKill", "Delete");
        OpenFolderShortcutGesture = settings.Keyboard.Shortcuts.GetValueOrDefault("ProcessMonitorOpenFolder", "Ctrl+E");
        _showSystemProcesses = settings.ProcessMonitor.ShowSystemProcesses;

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SelectedProcess) && SelectedProcess is { } process)
                _ = AnnounceSelectionAsync(process);
            else if (e.PropertyName == nameof(ShowSystemProcesses))
            {
                // Write-through to settings: this toolbar checkbox and the one in Settings
                // both mean "the ProcessMonitorSettings.ShowSystemProcesses value" - without
                // this, toggling it here would silently revert on next launch (the Settings
                // page's own checkbox only persists its own copy).
                _ = _settingsService.UpdateAsync(s => s.ProcessMonitor.ShowSystemProcesses = ShowSystemProcesses);
                _ = LoadAsync();
            }
        };

        LoadWhenWorkerReady(_workerConnection, FetchProcessesAsync, _logger, "Failed to load processes");
    }

    [ReactiveCommand]
    private async Task Refresh() => await LoadAsync();

    private Task LoadAsync() => RunLoadAsync(FetchProcessesAsync, _logger, "Failed to load processes");

    private async Task FetchProcessesAsync()
    {
        var processes = await _workerConnection.Client.SystemMonitor.EnumerateProcessesAsync(ShowSystemProcesses);
        _logger.LogDebug("Loaded {Count} processes (system processes {Included})",
            processes.Count, ShowSystemProcesses ? "included" : "excluded");
        Processes.Clear();
        foreach (var process in processes.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            Processes.Add(process);
    }

    public async Task KillSelectedAsync()
    {
        if (SelectedProcess is not { } process || process.IsSystemProcess) return;

        try
        {
            var result = await _workerConnection.Client.SystemMonitor.KillProcessAsync(process.Id);
            var text = result.Success
                ? ProcessAnnouncementFormatter.FormatKillSucceeded(process.Name, SpeechCulture)
                : ProcessAnnouncementFormatter.FormatKillFailed(process.Name, result.Error, SpeechCulture);
            await _speechService.AnnounceTextAsync(text, SpeechPriority.UserRequested);

            if (result.Success)
                Processes.Remove(process);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to end process {Name} ({Id})", process.Name, process.Id);
            await _speechService.AnnounceTextAsync(ProcessAnnouncementFormatter.FormatKillFailed(process.Name, ex.Message, SpeechCulture), SpeechPriority.UserRequested);
        }
    }

    [ReactiveCommand]
    private Task Kill() => KillSelectedAsync();

    public async Task OpenSelectedFolderAsync()
    {
        if (SelectedProcess is not { } process) return;

        try
        {
            var result = await _workerConnection.Client.SystemMonitor.OpenProcessFolderAsync(process.Id);
            if (!result.Success)
                await _speechService.AnnounceTextAsync(ProcessAnnouncementFormatter.FormatOpenFolderFailed(process.Name, result.Error, SpeechCulture), SpeechPriority.UserRequested);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open containing folder for {Name} ({Id})", process.Name, process.Id);
            await _speechService.AnnounceTextAsync(ProcessAnnouncementFormatter.FormatOpenFolderFailed(process.Name, ex.Message, SpeechCulture), SpeechPriority.UserRequested);
        }
    }

    [ReactiveCommand]
    private Task OpenFolder() => OpenSelectedFolderAsync();

    private async Task AnnounceSelectionAsync(ProcessDto process)
    {
        try
        {
            var detailed = _settingsService.Current.ProcessMonitor.AnnounceDetailedInfo;
            var text = ProcessAnnouncementFormatter.FormatSelection(process.Name, process.Id, detailed, process.MemoryBytes, SpeechCulture);

            // Best-effort: most processes own a top-level window, but a background/system one
            // may not - FindMainWindowForProcess returns null there, leaving this entry with no
            // window to replay-activate later (Announcement History already handles that case).
            var window = _windowEnumerator.FindMainWindowForProcess((uint)process.Id);
            await _speechService.AnnounceTextAsync(text, SpeechPriority.UserRequested, sourceWindowHandle: window?.Hwnd ?? default);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to announce selected process {Name}", process.Name);
        }
    }
}
