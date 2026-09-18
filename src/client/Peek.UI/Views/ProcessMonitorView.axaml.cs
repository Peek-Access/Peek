using AsyncNavigation.Abstractions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Peek.Core.ViewModels;
using System;
using System.Windows.Input;

namespace Peek.UI.Views;

public partial class ProcessMonitorView : UserControl, IView
{
    // Below this DataGrid width (this is what a docked strip's default 420px width, minus
    // margins, renders at), Memory/Description are too cramped to read - collapse to just
    // Process name + PID, which is what identifying/ending a specific process needs.
    private const double CompactWidthThreshold = 500.0;
    private bool? _isCompact;

    private ProcessMonitorViewModel? _vm;
    private KeyGesture? _killGesture;
    private KeyGesture? _openFolderGesture;

    public ProcessMonitorView()
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

    // See AppMonitorView's identical override - same cached-view reattach, same observed
    // stale-render workaround.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Grid.InvalidateArrange();
        Grid.InvalidateVisual();
    }

    private void ApplyColumnLayout(double width)
    {
        var compact = width < CompactWidthThreshold;
        if (_isCompact == compact) return;
        _isCompact = compact;

        // Column order: 0 Process name, 1 PID, 2 Memory, 3 Description.
        Grid.Columns[2].IsVisible = !compact;
        Grid.Columns[3].IsVisible = !compact;
    }

    private void TryAttachViewModel()
    {
        if (_vm is not null) return;
        if (DataContext is not ProcessMonitorViewModel vm) return;
        _vm = vm;

        _killGesture = ParseOrFallback(vm.KillShortcutGesture, "Delete");
        _openFolderGesture = ParseOrFallback(vm.OpenFolderShortcutGesture, "Ctrl+E");
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_vm is null || e.Handled) return;

        // ReactiveCommand<RxVoid, RxVoid> (this app's custom ReactiveUI.Primitives, not
        // standard ReactiveUI) implements ICommand explicitly - CanExecute/Execute aren't
        // directly callable without the cast.
        var killCommand = (ICommand)_vm.KillCommand;
        var openFolderCommand = (ICommand)_vm.OpenFolderCommand;

        if (_killGesture?.Matches(e) == true && killCommand.CanExecute(null))
        {
            killCommand.Execute(null);
            e.Handled = true;
        }
        else if (_openFolderGesture?.Matches(e) == true && openFolderCommand.CanExecute(null))
        {
            openFolderCommand.Execute(null);
            e.Handled = true;
        }
    }
}
