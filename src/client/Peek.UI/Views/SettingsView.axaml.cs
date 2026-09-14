using AsyncNavigation.Abstractions;
using Avalonia.Controls;
using Peek.Core.ViewModels;
using System;
using System.ComponentModel;

namespace Peek.UI.Views;

public partial class SettingsView : UserControl, IView
{
    private SettingsViewModel? _viewModel;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => Unsubscribe();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Unsubscribe();

        _viewModel = DataContext as SettingsViewModel;
        if (_viewModel is null) return;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateWindowAnnouncementOptionsOpacity();
    }

    private void Unsubscribe()
    {
        if (_viewModel is null) return;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.WindowAnnouncementEnabled))
            UpdateWindowAnnouncementOptionsOpacity();
    }

    /// <summary>
    /// Dims the Window Announcement sub-options while the master toggle is off.
    /// Driven from code-behind rather than XAML: the installed Pipboy.Avalonia
    /// package's CheckBox theme doesn't dim on IsEnabled=False the way
    /// ListBoxItem/TreeViewItem/ComboBox do, and - for reasons not worth chasing
    /// further given this is just as simple - a bound Opacity on the CheckBox
    /// itself never evaluated either, even via the same converter pattern already
    /// proven to work elsewhere in this file.
    /// </summary>
    private void UpdateWindowAnnouncementOptionsOpacity()
    {
        if (_viewModel is null) return;
        WindowAnnouncementOptionsPanel.Opacity = _viewModel.WindowAnnouncementEnabled ? 1.0 : 0.45;
    }
}
