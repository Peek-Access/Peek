using AsyncNavigation.Abstractions;
using Avalonia.Controls;
using Avalonia.Input;
using Peek.Core.ViewModels;
using System;
using System.Windows.Input;

namespace Peek.UI.Views;

public partial class AppMonitorView : UserControl, IView
{
    // Below this DataGrid width (this is what a docked strip's default 420px width, minus
    // margins, renders at), Version/Publisher/Disk usage are too cramped to read - collapse
    // to just Name, which is what matters for selecting an app to hear its details or
    // launch (see the toolbar's own help text).
    private const double CompactWidthThreshold = 500.0;
    private bool? _isCompact;

    private AppMonitorViewModel? _vm;
    private KeyGesture? _launchGesture;

    public AppMonitorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => TryAttachViewModel();
        Grid.SizeChanged += (_, e) => ApplyColumnLayout(e.NewSize.Width);
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        TryAttachViewModel();
    }

    private void ApplyColumnLayout(double width)
    {
        var compact = width < CompactWidthThreshold;
        if (_isCompact == compact) return;
        _isCompact = compact;

        // Column order: 0 Name, 1 Version, 2 Publisher, 3 Disk usage.
        Grid.Columns[1].IsVisible = !compact;
        Grid.Columns[2].IsVisible = !compact;
        Grid.Columns[3].IsVisible = !compact;
    }

    private void TryAttachViewModel()
    {
        if (_vm is not null) return;
        if (DataContext is not AppMonitorViewModel vm) return;
        _vm = vm;

        try
        {
            _launchGesture = KeyGesture.Parse(vm.LaunchShortcutGesture);
        }
        catch (NotSupportedException)
        {
            _launchGesture = KeyGesture.Parse("Enter");
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_vm is null || e.Handled) return;

        // ReactiveCommand<RxVoid, RxVoid> (this app's custom ReactiveUI.Primitives, not
        // standard ReactiveUI) implements ICommand explicitly - CanExecute/Execute aren't
        // directly callable without the cast.
        var command = (ICommand)_vm.LaunchCommand;
        if (_launchGesture?.Matches(e) == true && command.CanExecute(null))
        {
            command.Execute(null);
            e.Handled = true;
        }
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null) return;
        var command = (ICommand)_vm.LaunchCommand;
        if (command.CanExecute(null))
            command.Execute(null);
    }
}
