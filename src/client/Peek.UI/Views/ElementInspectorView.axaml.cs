using AsyncNavigation.Abstractions;
using Avalonia.Controls;
using Avalonia.Controls.DataGridHierarchical;
using Avalonia.Input;
using Peek.Core.ViewModels;
using System;
using System.Windows.Input;

namespace Peek.UI.Views;

public partial class ElementInspectorView : UserControl, IView
{
    private ElementInspectorViewModel? _vm;
    private KeyGesture? _expandGesture;

    // Below this DataGrid width, Control Type/Class/Process are too cramped to read (this
    // is what a docked strip's default 420px width, minus margins, renders at) - collapse
    // to just Name + Handle instead of four illegible slivers. Picked generously above the
    // docked default so a merely-narrow (not just docked) window gets the same treatment.
    private const double CompactWidthThreshold = 550.0;
    private bool? _isCompact;

    public ElementInspectorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => TryAttachViewModel();
        Tree.SizeChanged += (_, e) => ApplyColumnLayout(e.NewSize.Width);
    }

    public HierarchicalModel<InspectorNodeViewModel>? Model { get; private set; }

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

        // Column order: 0 Name, 1 Control Type, 2 Class, 3 Process, 4 Handle.
        Tree.Columns[1].IsVisible = !compact;
        Tree.Columns[2].IsVisible = !compact;
        Tree.Columns[3].IsVisible = !compact;
        Tree.Columns[4].IsVisible = compact;
    }

    private void TryAttachViewModel()
    {
        if (_vm is not null) return;
        if (DataContext is not ElementInspectorViewModel vm) return;
        _vm = vm;

        var options = new HierarchicalOptions<InspectorNodeViewModel>
        {
            ChildrenSelector = item => item.Children,
            // A node with nothing fetched yet still shows an expand arrow (optimistic -
            // UIA has no cheap "does this have children" check without walking it, and
            // this is a lazy tree: eagerly checking would defeat the point).
            IsLeafSelector = item => item.ChildrenLoaded && item.Children.Count == 0,
            IsExpandedSelector = item => item.IsExpanded,
            IsExpandedSetter = (item, value) => item.IsExpanded = value,
            AutoExpandRoot = false,
            VirtualizeChildren = true,
        };

        Model = new HierarchicalModel<InspectorNodeViewModel>(options);
        Model.SetRoots(vm.Roots);
        Tree.HierarchicalModel = Model;
        Model.NodeExpanded += OnNodeExpanded;

        // The expand/collapse shortcut is local to this control (only needs focus, not an
        // OS hook - see KeyboardSettings' doc comment). Unlike a Window, a UserControl has
        // no KeyBindings collection, so it's handled directly via OnKeyDown below - this
        // also means it naturally only fires while this monitor is the visible one,
        // whether hosted as a MainWindow tab or a dock strip page. Falls back to Space if
        // the user has typed an unparsable gesture into Settings.
        try
        {
            _expandGesture = KeyGesture.Parse(vm.ExpandShortcutGesture);
        }
        catch (NotSupportedException)
        {
            _expandGesture = KeyGesture.Parse("Space");
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_vm is null || e.Handled) return;

        // ReactiveCommand<RxVoid, RxVoid> (this app's custom ReactiveUI.Primitives, not
        // standard ReactiveUI) implements ICommand explicitly - CanExecute/Execute aren't
        // directly callable without the cast.
        var command = (ICommand)_vm.ToggleExpandSelectedCommand;
        if (_expandGesture?.Matches(e) == true && command.CanExecute(null))
        {
            command.Execute(null);
            e.Handled = true;
        }
    }

    private void OnNodeExpanded(object? sender, HierarchicalNodeEventArgs e)
    {
        if (_vm is null) return;
        if (e.Node.Item is InspectorNodeViewModel node)
            _ = _vm.ExpandNodeAsync(node);
    }

    private async void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm?.SelectedNode is not { } node) return;

        var element = await _vm.ResolveElementAsync(node);
        if (element is null) return;

        var detail = new ElementDetailWindow { DataContext = element };
        if (TopLevel.GetTopLevel(this) is Window owner)
            await detail.ShowDialog(owner);
        else
            detail.Show();
    }
}
