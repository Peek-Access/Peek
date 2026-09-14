using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Peek.Core.Abstractions;
using Peek.Core.Models;
using Peek.Core.ViewModels;
using Peek.UI.Views;
using System;
using System.Threading.Tasks;

namespace Peek.Views;

public partial class MainWindow : Window, IColorChangedNotify, IPeekSelfWindow
{
    private const double NormalTitleBarHeight = 34;
    private bool _isReallyExiting;
    private DockShellViewModel? _dockShellViewModel;
    private KeyGesture? _dockNextGesture;
    private KeyGesture? _dockPreviousGesture;

    // Plain cached bool, not a live property read - IPeekSelfWindow.IsMinimizedOrHidden is
    // polled from HighlightService's background watchdog timer, and reading an Avalonia
    // AvaloniaObject property (WindowState/IsVisible) off the UI thread throws (see
    // HideForScreenCaptureAsync's doc comment for the exact exception this app already hit
    // once from the same class of mistake). Updated only from OnPropertyChanged, which
    // Avalonia always raises on the UI thread, so this field itself never needs its own
    // locking/marshaling to be read safely from elsewhere.
    private volatile bool _isMinimizedOrHidden;

    public bool IsMinimizedOrHidden => _isMinimizedOrHidden;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        Opened += OnOpenedOrActivated;
        // Hide()/Show() (tray icon minimize-to-tray, see OnClosing) doesn't refire Opened -
        // Activated does, both on the initial show and every subsequent tray-triggered one,
        // so re-docking (removed in OnClosing) actually comes back each time in Docked mode.
        Activated += OnOpenedOrActivated;
        UpdateMinimizedOrHiddenFlag();
    }

    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty || change.Property == IsVisibleProperty)
            UpdateMinimizedOrHiddenFlag();
    }

    private void UpdateMinimizedOrHiddenFlag() =>
        _isMinimizedOrHidden = WindowState == WindowState.Minimized || !IsVisible;

    public void ChangeColor(ColorModel colorModel)
    {
        Pipboy.Avalonia.PipboyThemeManager.Instance.SetPrimaryColor(Color.FromRgb(colorModel.R, colorModel.G, colorModel.B));
    }

    /// <summary>Called from the tray icon's Exit command - the one path that should actually
    /// close (and thus shut down, via ShutdownMode.OnMainWindowClose) rather than hide.</summary>
    public void PrepareForRealExit() => _isReallyExiting = true;

    /// <summary>Shared by the title-bar (i) button and the tray menu's "About Peek..." item.</summary>
    public void ShowAbout()
    {
        var about = new AboutWindow { DataContext = new AboutViewModel() };
        about.ShowDialog(this);
    }

    private void OnAboutClick(object? sender, RoutedEventArgs e) => ShowAbout();

    /// <summary>
    /// Speaks the new pinned state so a screen-reader user - who can't see the button's
    /// checked-state color/glyph change - still knows whether the toggle just took effect.
    /// Delegated to MainViewModel (DataContext), which already holds the speech/settings
    /// services via its IServiceProvider constructor - Views in this app stay parameterless
    /// and get services through their ViewModel rather than DI-injected constructors (the
    /// latter breaks Avalonia's XAML previewer/hot-reload, which needs a public ctor it can
    /// call with no arguments).
    /// </summary>
    private void OnPinClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.AnnouncePinStateChanged(PinButton.IsChecked == true);
    }

    /// <summary>
    /// A full-desktop screenshot (see WindowsScreenshotService.CaptureDesktopAsync) is a
    /// raw BitBlt of the screen, so it would otherwise include Peek's own window - hiding
    /// it here and waiting a beat for the compositor to repaint whatever was behind it is
    /// the same trick ordinary screenshot tools use to exclude their own UI.
    /// </summary>
    /// <remarks>
    /// ScreenAnalysisService calls this/RestoreAfterScreenCapture with ConfigureAwait(false)
    /// throughout its own async chain (correct - it's mostly awaiting worker RPC calls) -
    /// by the time the screenshot RPC round-trip completes, execution has moved to whatever
    /// background thread completed that call, not necessarily the UI thread. Window.Show()/
    /// Hide() require the UI thread (reproduced as an InvalidOperationException deep in
    /// Window.ShowCore reading WindowDecorations off-thread), so both methods explicitly
    /// marshal onto it here rather than trusting the caller's thread.
    /// </remarks>
    public async Task HideForScreenCaptureAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(Hide);
        await Task.Delay(TimeSpan.FromMilliseconds(200));
    }

    public void RestoreAfterScreenCapture() => Dispatcher.UIThread.Post(Show);

    /// <summary>
    /// MainWindow itself is shell-mode-agnostic and its own DataContext is always
    /// MainViewModel (its title-bar chrome - the About/ColorPicker buttons - is compiled
    /// against that type; a second distinct DataContext type there reproducibly crashed
    /// during initial layout deep inside Avalonia/theme compiled XAML, even with that
    /// chrome hidden or the window's own content emptied out - see git history for the
    /// investigation). Docked mode instead gets its own DockShellViewModel explicitly
    /// assigned as DockShellView's DataContext below, decoupled from the Window's own.
    /// </summary>
    public void ConfigureShell(MainViewModel mainViewModel, DockShellViewModel? dockShellViewModel)
    {
        DataContext = mainViewModel;
        _dockShellViewModel = dockShellViewModel;

        if (dockShellViewModel is not null)
        {
            DockedShellView.DataContext = dockShellViewModel;
            DockedShellView.IsVisible = true;
            NormalShellView.IsVisible = false;
            _dockNextGesture = ParseOrFallback(dockShellViewModel.NextShortcutGesture, "PageDown");
            _dockPreviousGesture = ParseOrFallback(dockShellViewModel.PreviousShortcutGesture, "PageUp");

            // Docked mode is already forced HWND_TOPMOST at the Win32 level by
            // WindowsEdgeDockingService, independent of this Window's Topmost property -
            // the pin toggle would do nothing visible there, so hide it to avoid confusion.
            PinButton.IsVisible = false;
            ApplyShellChrome(isDocked: true);
        }
        else
        {
            NormalShellView.IsVisible = true;
            DockedShellView.IsVisible = false;
            PinButton.IsVisible = true;
            ApplyShellChrome(isDocked: false);
        }
    }

    /// <summary>
    /// Keeps the native decoration mode and the app-drawn title-bar row in sync. BorderOnly
    /// removes the OS title bar in docked mode; collapsing the row as well prevents the
    /// hidden title-bar controls from leaving a blank strip above DockShellView. Normal mode
    /// restores both pieces so the regular title bar and its action buttons return together.
    /// </summary>
    private void ApplyShellChrome(bool isDocked)
    {
        TitleBarButtons.IsVisible = !isDocked;
        WindowLayout.RowDefinitions[0].Height = isDocked
            ? new GridLength(0)
            : new GridLength(NormalTitleBarHeight);
        WindowDecorations = isDocked ? WindowDecorations.BorderOnly : WindowDecorations.Full;
        ExtendClientAreaToDecorationsHint = true;
    }

    private static KeyGesture ParseOrFallback(string gesture, string fallback)
    {
        try
        {
            return KeyGesture.Parse(gesture);
        }
        catch (NotSupportedException)
        {
            return KeyGesture.Parse(fallback);
        }
    }

    /// <summary>
    /// Docked-mode PageUp/PageDown monitor cycling - handled here (not in DockShellView
    /// itself) so it fires for every unhandled key press regardless of which descendant
    /// inside the currently-navigated content happens to have focus. A Window override is
    /// the last stop in the bubble phase, so this only misses a key a focused descendant
    /// control has already consumed for itself.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_dockShellViewModel is null || e.Handled) return;

        if (_dockNextGesture?.Matches(e) == true)
        {
            _ = _dockShellViewModel.NavigateNextAsync();
            e.Handled = true;
        }
        else if (_dockPreviousGesture?.Matches(e) == true)
        {
            _ = _dockShellViewModel.NavigatePreviousAsync();
            e.Handled = true;
        }
    }

    private void OnOpenedOrActivated(object? sender, EventArgs e)
    {
        if (_dockShellViewModel is null) return;
        var handle = TryGetPlatformHandle()?.Handle ?? 0;
        if (handle != 0)
            _dockShellViewModel.ApplyDocking(handle);

        // Avalonia's own keyboard focus is independent of the OS's foreground-window
        // concept - clicking empty space inside the dock strip (nothing Focusable there)
        // leaves NO element focused, so OnKeyDown below never fires for PageUp/PageDown at
        // all even though Win32 correctly reports this as the foreground window. Focusing
        // the Window itself here guarantees it holds focus by default whenever it's
        // activated, so the shortcut works without first clicking a specific control.
        Focus();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isReallyExiting) return;

        // Peek is meant to stay available in the background (hover/announcement
        // tracking keeps running) the same way NVDA and other always-on
        // accessibility tools do - closing the window just hides it; the tray
        // icon's Exit item is the only way to actually quit.
        e.Cancel = true;
        _dockShellViewModel?.RemoveDocking();
        Hide();
    }
}
