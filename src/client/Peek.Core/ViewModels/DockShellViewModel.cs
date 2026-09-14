using AsyncNavigation.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Peek.Core.Abstractions;
using Peek.Core.Services;
using Peek.Core.Settings;
using ReactiveUI.SourceGenerators;

namespace Peek.Core.ViewModels;

/// <summary>
/// Owns the docked shell window's own concerns - edge docking and cycling between the four
/// monitor views - as distinct from any one monitor's own ViewModel. See DockShellSettings
/// for why this exists: reserving a permanent screen strip is what lets selecting an
/// element bring its window forward (see ElementInspectorViewModel.OnNodeSelectedAsync)
/// without shoving the dock strip itself out of view.
/// </summary>
/// <summary>
/// Deliberately derives from ViewModelBase (ReactiveObject/INotifyPropertyChanged) like
/// every other ViewModel in this codebase, even though nothing here is [Reactive]: as a
/// bare class it doesn't implement INotifyPropertyChanged, and Avalonia's compiled
/// bindings/style infrastructure eagerly wires up property-change tracking for whatever
/// type an x:DataType root declares - without that interface, the moment DockShellView
/// (x:DataType="vm:DockShellViewModel") is shown, that wiring threw
/// InvalidCastException deep inside a compiled XamlDynamicSetter during the very first
/// layout pass, regardless of DockShellView's own content (reproduced with content
/// stripped to nothing). Cost a lot of session time to root-cause - see git history.
/// </summary>
public partial class DockShellViewModel : ViewModelBase
{
    /// <summary>Fixed rotation order for PageUp/PageDown - Settings is reachable separately (see NavigateToSettingsAsync) and deliberately isn't part of the cycle.</summary>
    public static readonly string[] MonitorViewNames =
    [
        "ScreenReaderView",
        "ElementInspectorView",
        "AppMonitorView",
        "ProcessMonitorView",
    ];

    // Deliberately distinct from MainView's "MainRegion" - both MainView and DockShellView
    // are declared statically side by side in MainWindow.axaml (see MainWindow.axaml.cs),
    // so each needs its own region name to avoid two hosts racing to register the same one.
    private const string RegionName = "DockRegion";

    private readonly IEdgeDockingService _dockingService;
    private readonly ISettingsService _settingsService;
    private readonly IRegionManager _regionManager;
    private readonly MonitorSwitchSoundPlayer _switchSoundPlayer;

    private string _currentViewName = MonitorViewNames[0];

    /// <summary>1-based position of the current monitor within <see cref="MonitorViewNames"/>, for the dock header's "N / count" indicator - 0 while a non-cycled view (e.g. Settings) is showing.</summary>
    [Reactive]
    private int _currentMonitorPosition = 1;

    public int MonitorCount => MonitorViewNames.Length;

    public string NextShortcutGesture { get; }
    public string PreviousShortcutGesture { get; }

    public DockShellViewModel(IServiceProvider serviceProvider)
    {
        _dockingService = serviceProvider.GetRequiredService<IEdgeDockingService>();
        _settingsService = serviceProvider.GetRequiredService<ISettingsService>();
        _regionManager = serviceProvider.GetRequiredService<IRegionManager>();
        _switchSoundPlayer = serviceProvider.GetRequiredService<MonitorSwitchSoundPlayer>();

        var shortcuts = _settingsService.Current.Keyboard.Shortcuts;
        NextShortcutGesture = shortcuts.GetValueOrDefault("DockMonitorNext", "PageDown");
        PreviousShortcutGesture = shortcuts.GetValueOrDefault("DockMonitorPrevious", "PageUp");

        if (_regionManager.TryGetRegion(RegionName, out var region))
        {
            region.Navigated += (_, e) =>
            {
                if (e.Context.ViewName is { } name)
                {
                    _currentViewName = name;
                    var index = Array.IndexOf(MonitorViewNames, name);
                    CurrentMonitorPosition = index < 0 ? 0 : index + 1;
                }
            };
        }
    }

    public void ApplyDocking(nint windowHandle)
    {
        var settings = _settingsService.Current.DockShell;
        _dockingService.Apply(windowHandle, settings.DockEdge, settings.DockWidth);
    }

    public void RemoveDocking() => _dockingService.Remove();

    public Task NavigateToFirstMonitorAsync() => _regionManager.RequestNavigateAsync(RegionName, MonitorViewNames[0]);

    public Task NavigateNextAsync() => NavigateByOffsetAsync(1);

    public Task NavigatePreviousAsync() => NavigateByOffsetAsync(-1);

    public Task NavigateToSettingsAsync() => _regionManager.RequestNavigateAsync(RegionName, "SettingsView");

    private async Task NavigateByOffsetAsync(int offset)
    {
        var currentIndex = Array.IndexOf(MonitorViewNames, _currentViewName);
        // Not currently on one of the four monitors (e.g. Settings is showing) - PageDown/
        // PageUp both just land back on the first monitor rather than computing a
        // meaningless offset from an index of -1.
        var nextIndex = currentIndex < 0
            ? 0
            : ((currentIndex + offset) % MonitorViewNames.Length + MonitorViewNames.Length) % MonitorViewNames.Length;

        await _regionManager.RequestNavigateAsync(RegionName, MonitorViewNames[nextIndex]);

        if (_settingsService.Current.DockShell.PageSwitchSoundEnabled)
            _ = _switchSoundPlayer.PlayAsync();
    }
}
