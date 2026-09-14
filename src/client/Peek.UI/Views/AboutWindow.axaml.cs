using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Peek.UI.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    // A modal dialog with no default-focused control otherwise leaves keyboard
    // users with no discoverable way to dismiss it besides tabbing all the way to
    // the Close button - Escape is the standard, expected way out.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && !e.Handled)
        {
            Close();
            e.Handled = true;
        }
    }
}
