using Avalonia.Controls;
using Avalonia.Interactivity;
using Peek.Core.ViewModels;
using Peek.Views;

namespace Peek.UI.Views;

/// <summary>
/// Docked-mode content for MainWindow: the same four monitor views MainView's nav sidebar
/// shows in normal mode, hosted in a region and cycled with a keyboard shortcut instead of
/// clicking a nav item - see DockShellViewModel and DockShellSettings.Mode. The
/// PageUp/PageDown handling itself lives on MainWindow, not here (see
/// MainWindow.OnKeyDown) - it needs to fire regardless of which descendant control inside
/// the navigated content currently has focus, which only a Window-level handler reliably
/// sees for every unhandled key press.
/// </summary>
/// <remarks>
/// Deliberately a UserControl, not its own Window: a second distinct Window subclass shown
/// via AsyncNavigation's FrontShowWindowAsync-returned-window path crashed during initial
/// layout (InvalidCastException deep in a compiled XamlDynamicSetter, reproduced even with
/// an empty content Grid - not this control's own content) - MainWindow is the only window
/// type ever proven to work through that path, so docked mode reuses it with this control
/// swapped in for MainView (see MainWindow.axaml.cs).
/// </remarks>
public partial class DockShellView : UserControl
{
    private DockShellViewModel? _vm;

    public DockShellView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => TryAttachViewModel();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        TryAttachViewModel();
    }

    private void TryAttachViewModel()
    {
        if (_vm is not null) return;
        if (DataContext is not DockShellViewModel vm) return;
        _vm = vm;

        _ = vm.NavigateToFirstMonitorAsync();
    }

    private void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow owner)
            owner.ShowAbout();
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e) => _ = _vm?.NavigateToSettingsAsync();

    private void OnPreviousClick(object? sender, RoutedEventArgs e) => _ = _vm?.NavigatePreviousAsync();

    private void OnNextClick(object? sender, RoutedEventArgs e) => _ = _vm?.NavigateNextAsync();
}
