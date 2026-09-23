using AsyncNavigation.Abstractions;
using Avalonia.Controls;
using Avalonia.Input;
using Peek.Core.ViewModels;
using System.Windows.Input;

namespace Peek.UI.Views;

public partial class DockAnnouncementView : UserControl, IView
{
    private AnnouncementViewModel? _vm;

    public DockAnnouncementView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => _vm = DataContext as AnnouncementViewModel;
    }

    private void OnHistoryRowDoubleTapped(object? sender, TappedEventArgs e) => TryReplay();

    private void OnHistoryListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.Handled) return;
        TryReplay();
        e.Handled = true;
    }

    private void TryReplay()
    {
        if (_vm is null) return;

        var command = (ICommand)_vm.ReplaySelectedCommand;
        if (command.CanExecute(null))
            command.Execute(null);
    }
}
