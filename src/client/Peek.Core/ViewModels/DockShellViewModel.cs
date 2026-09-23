using System.Globalization;
using AsyncNavigation.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Peek.Core.Abstractions;
using Peek.Core.i18n;
using Peek.Core.Services;
using Peek.Core.Services.Speech;
using Peek.Core.Settings;
using ReactiveUI.SourceGenerators;

namespace Peek.Core.ViewModels;

/// <summary>
/// Owns the docked shell window's own concerns - edge docking and cycling between the four
/// monitor views plus Settings - as distinct from any one monitor's own ViewModel. See DockShellSettings
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
    /// <summary>Fixed rotation order for PageUp/PageDown. Settings is the final page in the cycle.</summary>
    public static readonly string[] MonitorViewNames =
    [
        "ScreenReaderView",
        "ElementInspectorView",
        "AppMonitorView",
        "ProcessMonitorView",
        "SettingsView",
    ];

    // Deliberately distinct from MainView's "MainRegion" - both MainView and DockShellView
    // are declared statically side by side in MainWindow.axaml (see MainWindow.axaml.cs),
    // so each needs its own region name to avoid two hosts racing to register the same one.
    private const string RegionName = "DockRegion";
    private const string AnnouncementRegionName = "DockAnnouncementRegion";

    private readonly IEdgeDockingService _dockingService;
    private readonly ISettingsService _settingsService;
    private readonly IRegionManager _regionManager;
    private readonly MonitorSwitchSoundPlayer _switchSoundPlayer;
    private readonly IAccessibilitySpeechService _speechService;

    private string _currentViewName = MonitorViewNames[0];

    /// <summary>1-based position of the current page within <see cref="MonitorViewNames"/>.</summary>
    [Reactive]
    private int _currentMonitorPosition = 1;

    /// <summary>Title shown in the dock header for the current page.</summary>
    [Reactive]
    private string _currentPageTitle = "Screen Reader";

    public int MonitorCount => MonitorViewNames.Length;

    public string NextShortcutGesture { get; }
    public string PreviousShortcutGesture { get; }

    public DockShellViewModel(IServiceProvider serviceProvider)
    {
        _dockingService = serviceProvider.GetRequiredService<IEdgeDockingService>();
        _settingsService = serviceProvider.GetRequiredService<ISettingsService>();
        _regionManager = serviceProvider.GetRequiredService<IRegionManager>();
        _switchSoundPlayer = serviceProvider.GetRequiredService<MonitorSwitchSoundPlayer>();
        _speechService = serviceProvider.GetRequiredService<IAccessibilitySpeechService>();

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
                    CurrentPageTitle = NavViewTitles.Resolve(name, ResolveUiCulture());
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

    /// <summary>
    /// Called once, when the docked shell first opens (Docked is now the default shell mode -
    /// see DockShellSettings) - announces the landing page the same way PageUp/PageDown/
    /// Settings navigation already does, so a fresh launch doesn't leave the user wondering
    /// which of the four monitors (or Settings) they've opened into.
    /// </summary>
    public async Task NavigateToFirstMonitorAsync()
    {
        await _regionManager.RequestNavigateAsync(RegionName, MonitorViewNames[0]);
        AnnounceCurrentPage(MonitorViewNames[0]);
    }

    public Task NavigateToAnnouncementsAsync() =>
        _regionManager.RequestNavigateAsync(AnnouncementRegionName, "DockAnnouncementView");

    public Task NavigateNextAsync() => NavigateByOffsetAsync(1);

    public Task NavigatePreviousAsync() => NavigateByOffsetAsync(-1);

    public async Task NavigateToSettingsAsync()
    {
        await _regionManager.RequestNavigateAsync(RegionName, "SettingsView");
        AnnounceCurrentPage("SettingsView");
    }

    private async Task NavigateByOffsetAsync(int offset)
    {
        var currentIndex = Array.IndexOf(MonitorViewNames, _currentViewName);
        // If an unknown view is showing, restart the cycle at the first page.
        var nextIndex = currentIndex < 0
            ? 0
            : ((currentIndex + offset) % MonitorViewNames.Length + MonitorViewNames.Length) % MonitorViewNames.Length;

        await _regionManager.RequestNavigateAsync(RegionName, MonitorViewNames[nextIndex]);
        AnnounceCurrentPage(MonitorViewNames[nextIndex]);

        if (_settingsService.Current.DockShell.PageSwitchSoundEnabled)
            _ = _switchSoundPlayer.PlayAsync();
    }

    /// <summary>
    /// Speaks the page PageUp/PageDown/Settings just switched to. Needed specifically for
    /// this cycling path: unlike clicking a nav item, cycling never moves keyboard focus onto
    /// anything - MainWindow itself holds focus throughout (see MainWindow.OnKeyDown) - so
    /// SelfFocusAnnouncer never sees a focus change here to announce on its own.
    /// </summary>
    private void AnnounceCurrentPage(string viewName)
    {
        if (!_settingsService.Current.Accessibility.AnnounceOwnInterface) return;

        var culture = SpeechStrings.ResolveCulture(_settingsService.Current.Localization);
        _ = _speechService.AnnounceTextAsync(NavViewTitles.Resolve(viewName, culture), SpeechPriority.UserRequested);
    }

    private CultureInfo ResolveUiCulture()
    {
        try
        {
            return CultureInfo.GetCultureInfo(_settingsService.Current.Localization.UiLanguage);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
