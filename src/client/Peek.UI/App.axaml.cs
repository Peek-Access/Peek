using AsyncNavigation;
using AsyncNavigation.Abstractions;
using AsyncNavigation.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Irihi.Lingua;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Peek.Core.Abstractions;
using Peek.Core.i18n;
using Peek.Core.Diagnostics;
using Peek.Core.Logging;
using Peek.Core.Models;
using Peek.Core.Services;
using Peek.Core.Services.Llm;
using Peek.Core.Services.Ocr;
using Peek.Core.Services.Speech;
using Peek.Core.Settings;
using Peek.Core.ViewModels;
using Peek.Ipc.DependencyInjection;
using Peek.UI.Indicators;
using Peek.UI.Views;
using Peek.Views;
using Pipboy.Avalonia;
using ReactiveUI;
using ReactiveUI.Primitives;
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace Peek.UI;

[SupportedOSPlatform("windows7.0")]
public partial class App : Application
{
    private ILogger<App>? _logger;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += Dispatcher_UnhandledException;

        NavigationOptions navigationOptions = new()
        {
            /// default is CancelCurrent <see cref="NavigationJobStrategy.CancelCurrent"/>
            NavigationJobStrategy = NavigationJobStrategy.CancelCurrent,
            LoadingIndicatorDelay = TimeSpan.FromMilliseconds(1)
        };
        var services = new ServiceCollection();
        services.AddNavigationSupport(navigationOptions)
                .RegisterDialogWindow<SplashWindow, SplashViewModel>("SplashWindow")
                .RegisterView<ScreenReaderView, ScreenReaderViewModel>(nameof(ScreenReaderView))
                .RegisterView<AnnouncementView, AnnouncementViewModel>(nameof(AnnouncementView))
                .RegisterView<DockAnnouncementView, AnnouncementViewModel>(nameof(DockAnnouncementView))
                .RegisterView<ElementInspectorView, ElementInspectorViewModel>(nameof(ElementInspectorView))
                .RegisterView<AppMonitorView, AppMonitorViewModel>(nameof(AppMonitorView))
                .RegisterView<ProcessMonitorView, ProcessMonitorViewModel>(nameof(ProcessMonitorView))
                .RegisterView<SettingsView, SettingsViewModel>(nameof(SettingsView))
                .RegisterInnerIndicatorProvider<ProgressIndicatorProvider>()
                .AddSingleton<MainWindow>()
                .AddSingleton<DockShellViewModel>()
                .AddSingleton<ILinguaManager>(LanguageManager.Instance)
                .AddSingleton<ElementTracker>()
                // Singleton: shared by WindowAnnouncer (focus-change announcements) and
                // ElementInspectorViewModel (window list), so neither registers a
                // duplicate OS hook and the window list is available the instant the
                // Inspector monitor is navigated to with no separate warm-up.
                .AddSingleton<WindowTracker>()
                .AddSingleton<WindowLifecycleTracker>()
                .AddSingleton<NotificationSoundPlayer>()
                .AddSingleton<MonitorSwitchSoundPlayer>()
                .AddSingleton<AiNarrationSoundPlayer>()
                .AddSingleton<AnnouncementHistoryService>()
                .AddSingleton<ScreenAnalysisService>()
                .AddSingleton<WindowAnnouncer>()
                .AddSingleton<IDisposeService, DisposeService>()
                .AddTransient<LoadingIndicator>()
                .AddSingleton<WindowsHookService>()
                .AddSingleton<GlobalHotkeyService>()
                .AddSingleton<FocusTracker>()
                .AddSingleton<FocusAnnouncer>()
                .AddSingleton<Peek.UI.Services.SelfFocusAnnouncer>()
                .AddSingleton<AudioPlayer>()
                .AddSingleton<IColorChangedNotify, MainWindow>(sp => sp.GetRequiredService<MainWindow>())
                .AddSingleton<IPeekSelfWindow, MainWindow>(sp => sp.GetRequiredService<MainWindow>())
                .AddSingleton<ISpeechPolicy, StandardSpeechPolicy>()
                .AddSingleton<IAccessibilitySpeechService, AccessibilitySpeechService>()
                .AddSingleton<IOcrDecisionService, DefaultOcrDecisionService>()
                .AddSingleton<OcrFallbackAnnouncer>()
                .AddSingleton<IContextAggregator, ContextAggregator>()
                .AddSingleton<IElementDescriptionService, ElementDescriptionService>()
                .AddSingleton<ISettingsService, JsonSettingsService>()
                .AddSingleton<IClipboardService, AvaloniaClipboardProvider>()
                .AddSingleton<ColorPickerViewModel>()
                .AddSingleton<WindowEnumerator>()
                .AddSingletonWithAllMembers<MainViewModel>()
                .AddSingleton<IHighlightOverlay, HighlightOverlay>()
                .AddSingleton<IHighlightService, HighlightService>()
                // Its own service and window, separate from the highlight - see IAiAnalysisEffect.
                .AddSingleton<IAiAnalysisEffect, AiAnalysisEffect>()
                .AddSingleton(PipboyThemeManager.Instance)
                .AddLogging(builder =>
                  {
                      builder.ClearProviders();

#if DEBUG
                      // Debug-only: Microsoft.Extensions.Logging.Console's SimpleConsole
                      // writer isn't safe under the write volume/concurrency a Release
                      // install runs windowed (no console) anyway (see Peek.Desktop.csproj) -
                      // a real crash was reproduced from it: System.InvalidOperationException
                      // "The stream is currently in use by a previous operation on the
                      // stream." out of StreamWriter.Flush, caught live in Windows Event
                      // Viewer during testing. Release relies solely on the file sink below.
                      builder.AddSimpleConsole(options =>
                      {
                          options.IncludeScopes = true;
                          options.SingleLine = true;
                          options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
                          options.UseUtcTimestamp = false;
                          options.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
                      });
#endif

                      // A packaged Release build runs windowed (see Peek.Desktop.csproj) -
                      // a Console-subsystem exe launched directly from a shortcut auto-
                      // allocates a visible console window, which is exactly the wall of
                      // scrolling Debug-level log text a user mistook for "lots of errors".
                      // Without this file sink, going windowed would silently throw away
                      // every diagnostic for an installed app instead.
                      builder.AddProvider(new SimpleFileLoggerProvider("peek-client"));

                      // Information by default; set PEEK_LOG_LEVEL=Debug to raise it on an
                      // installed build without a rebuild - see PeekLogLevel. The worker
                      // inherits the same variable when the client launches it.
                      builder.SetMinimumLevel(PeekLogLevel.Resolve());
                  })
                .AddPeekWorker();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
#pragma warning disable CA1416 // Validate platform compatibility
            services.AddSingleton<IMouseTracker, WindowsMouseTracker>();
            services.AddSingleton<IEdgeDockingService, WindowsEdgeDockingService>();
#pragma warning restore CA1416 // Validate platform compatibility
        }
        else
        {
            throw new NotSupportedException($"Unsupported OS:{RuntimeInformation.OSDescription}");
        }

        var sp = services.BuildServiceProvider();
        _logger = sp.GetRequiredService<ILogger<App>>();

        // Settings must be loaded - and the persisted UI language applied - before
        // any view is constructed, since navigation to the default view happens
        // almost immediately and SettingsView (the only other place culture was
        // previously ever touched) may never even be opened this run. Uses the
        // synchronous Load() rather than blocking on LoadAsync() here: the
        // Avalonia dispatcher loop isn't pumping yet at this point in startup, so
        // GetAwaiter().GetResult() on the async path can deadlock waiting for a
        // continuation the not-yet-running dispatcher would otherwise service.
        var settingsService = sp.GetRequiredService<ISettingsService>();
        settingsService.Load();
        try
        {
            var uiLanguage = new CultureInfo(settingsService.Current.Localization.UiLanguage);
            sp.GetRequiredService<ILinguaManager>().UpdateCulture(uiLanguage);
        }
        catch (CultureNotFoundException ex)
        {
            _logger.LogWarning(ex, "Persisted UI language '{Culture}' is not a valid culture - using the default",
                settingsService.Current.Localization.UiLanguage);
        }

        // Restore the picked-in-Settings theme color before any window is shown, so it
        // doesn't flash the theme's built-in default first. ColorPickerViewModel is a DI
        // singleton (also injected into SettingsViewModel) - resolving it here rather than
        // constructing a separate instance means the picker UI and this startup restore
        // always agree on the current color.
        var colorPicker = sp.GetRequiredService<ColorPickerViewModel>();
        if (ColorModel.TryParse(settingsService.Current.Appearance.ThemeColor, out var themeColor))
        {
            PipboyThemeManager.Instance.SetPrimaryColor(Avalonia.Media.Color.FromRgb(themeColor.R, themeColor.G, themeColor.B));
            colorPicker.SelectedColor = themeColor;
        }
        // Skip(1): WhenAnyValue replays the current value to a new subscriber immediately -
        // without this, the restore above would immediately re-persist the same value it
        // just loaded.
        colorPicker.WhenAnyValue(x => x.SelectedColor)
            .Skip(1)
            .Subscribe(color => _ = settingsService.UpdateAsync(s => s.Appearance.ThemeColor = color.ToHex()));

        // Eagerly start the window open/close/focus-change announcer here rather than
        // relying on some view's constructor to resolve it incidentally (the way
        // ElementTracker/WindowTracker otherwise would): it must run for the whole app
        // lifetime regardless of which view is currently shown, since it's gated purely
        // by WindowAnnouncementSettings, not by navigation.
        sp.GetRequiredService<WindowAnnouncer>();

        // Same reasoning as WindowAnnouncer above: the global keyboard shortcuts
        // (DescribeFocusedElement/ToggleTracking/StopSpeaking) must work regardless of which
        // view is navigated to, or even while some other application is focused - nothing
        // else would otherwise ever resolve this service.
        sp.GetRequiredService<GlobalHotkeyService>();

        // Follow-the-keyboard announcements, likewise app-lifetime and gated purely by
        // AccessibilitySettings.AnnounceOnFocus rather than by navigation - this is the
        // primary interaction model for a user who can't aim a mouse at what they can't see,
        // so it must be running from startup, not from whenever a particular view happens to
        // be opened.
        sp.GetRequiredService<FocusAnnouncer>();

        // Peek reading its own UI as the user tabs through it - see SelfFocusAnnouncer's own
        // doc comment for why this is a separate mechanism from FocusAnnouncer above rather
        // than just removing that one's own-process exclusion. App-lifetime for the same
        // reason as FocusAnnouncer: it must work in every window Peek ever opens, not just
        // whichever view happens to be navigated to right now.
        sp.GetRequiredService<Peek.UI.Services.SelfFocusAnnouncer>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dialogService = sp.GetRequiredService<IDialogService>();
            dialogService.FrontShowWindowAsync<Window>("SplashWindow", result =>
            {
                if (result.Result == DialogButtonResult.Done)
                {
                    // Decided once at startup, not switched live - see DockShellSettings.Mode.
                    // Both modes reuse the same MainWindow instance/type (see
                    // MainWindow.axaml.cs and DockShellView's doc comment for why a second
                    // Window subclass isn't used); MainView/DockShellView are declared
                    // statically side by side there, each under its own region name
                    // ("MainRegion" / "DockRegion" - see DockShellViewModel).
                    var mainWindow = sp.GetRequiredService<MainWindow>();
                    var isDocked = settingsService.Current.DockShell.Mode == ShellMode.Docked;
                    mainWindow.ConfigureShell(
                        sp.GetRequiredService<MainViewModel>(),
                        isDocked ? sp.GetRequiredService<DockShellViewModel>() : null);
                    desktop.MainWindow = mainWindow;
                    desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
                    desktop.Exit += OnExit;
                    SetupTrayIcon(sp, desktop, mainWindow, mainWindow.ShowAbout, mainWindow.PrepareForRealExit, isDocked);
                    return mainWindow;
                }
                else
                {
                    if (Current?.ApplicationLifetime is IControlledApplicationLifetime applicationLifetime)
                    {
                        applicationLifetime.Shutdown();
                    }
                    return null;
                }
            });

        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = () => new MainView { DataContext = sp.GetRequiredService<MainViewModel>() };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = sp.GetRequiredService<MainViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogFatal(e.Exception, "Unobserved task exception (a fire-and-forget async call failed)");
        WriteCrashReport(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }

    private void Dispatcher_UnhandledException(object? sender, Avalonia.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogFatal(e.Exception, "Unhandled exception on the UI thread");
        WriteCrashReport(e.Exception, "UI thread");
        // Keep the app alive so the failure is visible instead of silently killing the process.
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        LogFatal(e.ExceptionObject as Exception, "Unhandled AppDomain exception (process is terminating: {IsTerminating})", e.IsTerminating);
        WriteCrashReport(e.ExceptionObject as Exception, $"AppDomain (terminating: {e.IsTerminating})");
    }

    /// <summary>
    /// Leaves a standalone, readable crash file alongside the logs. The rolling log already
    /// records the exception, but it's buried under normal Debug traffic within minutes -
    /// this is the artifact a user can actually find and attach to an issue. Local only; see
    /// <see cref="CrashReporter"/> for why nothing is transmitted.
    /// </summary>
    private void WriteCrashReport(Exception? ex, string context)
    {
        var path = CrashReporter.Write(ex, context);
        if (path is not null)
            _logger?.LogInformation("Crash report written to {Path}", path);
    }

    private void LogFatal(Exception? ex, string message, params object?[] args)
    {
        if (_logger is not null)
            _logger.LogCritical(ex, message, args);
        else
            Console.Error.WriteLine($"{message}: {ex}");
    }

    void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = desktop.MainWindow?.DataContext as IDisposable;
            vm?.Dispose();
        }
    }

    /// <summary>
    /// Peek is meant to run in the background the same way NVDA/other always-on
    /// accessibility tools do - MainWindow.Closing hides rather than closes (see
    /// MainWindow.axaml.cs), and this tray icon is the persistent entry point back
    /// into it plus the one real "Exit" path. Built in code rather than declared in
    /// App.axaml: the menu's Tracking item needs to react live to
    /// ElementTracker.IsTracking, which is simpler to wire as a subscription here
    /// than through NativeMenu's XAML data-binding support.
    /// </summary>
    private static void SetupTrayIcon(
        IServiceProvider sp,
        IClassicDesktopStyleApplicationLifetime desktop,
        Window shellWindow,
        Action showAbout,
        Action prepareForRealExit,
        bool isDocked)
    {
        var settingsRegionName = isDocked ? "DockRegion" : "MainRegion";
        var elementTracker = sp.GetRequiredService<ElementTracker>();
        var regionManager = sp.GetRequiredService<IRegionManager>();

        void ShowShellWindow()
        {
            shellWindow.Show();
            shellWindow.WindowState = Avalonia.Controls.WindowState.Normal;
            shellWindow.Activate();
        }

        var showItem = new NativeMenuItem("Show Peek");
        showItem.Click += (_, _) => ShowShellWindow();

        var trackingItem = new NativeMenuItem("Tracking") { ToggleType = MenuItemToggleType.CheckBox, IsChecked = elementTracker.IsTracking };
        trackingItem.Click += (_, _) => elementTracker.IsTracking = !elementTracker.IsTracking;
        elementTracker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ElementTracker.IsTracking))
                trackingItem.IsChecked = elementTracker.IsTracking;
        };

        var settingsItem = new NativeMenuItem("Settings...");
        settingsItem.Click += async (_, _) =>
        {
            ShowShellWindow();
            await regionManager.RequestNavigateAsync(settingsRegionName, "SettingsView");

            // The native tray menu is outside Avalonia's own focus system, so opening
            // Settings from it moves nothing Avalonia considers focused - SelfFocusAnnouncer
            // never sees a change here to announce on its own (same reasoning as
            // DockShellViewModel.AnnounceCurrentPage for PageUp/PageDown).
            var settings = sp.GetRequiredService<ISettingsService>();
            if (settings.Current.Accessibility.AnnounceOwnInterface)
            {
                var culture = SpeechStrings.ResolveCulture(settings.Current.Localization);
                _ = sp.GetRequiredService<IAccessibilitySpeechService>()
                    .AnnounceTextAsync(NavViewTitles.Resolve("SettingsView", culture), SpeechPriority.UserRequested);
            }
        };

        var aboutItem = new NativeMenuItem("About Peek...");
        aboutItem.Click += (_, _) => showAbout();

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            prepareForRealExit();
            desktop.Shutdown();
        };

        var menu = new NativeMenu
        {
            showItem,
            trackingItem,
            new NativeMenuItemSeparator(),
            settingsItem,
            aboutItem,
            new NativeMenuItemSeparator(),
            exitItem,
        };

        using var iconStream = AssetLoader.Open(new Uri("avares://Peek.UI/Assets/peek-icon.ico"));
        var trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "Peek",
            Menu = menu,
            IsVisible = true,
        };
        trayIcon.Clicked += (_, _) => ShowShellWindow();

        TrayIcon.SetIcons(Current!, new TrayIcons { trayIcon });
    }
}
