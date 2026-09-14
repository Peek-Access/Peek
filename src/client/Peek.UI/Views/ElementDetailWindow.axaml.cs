using Avalonia.Controls;
using Avalonia.Input;

namespace Peek.UI.Views;

public partial class ElementDetailWindow : Window
{
    public ElementDetailWindow()
    {
        InitializeComponent();
    }

    // Usually shown via ShowDialog (see ElementInspectorView.OnRowDoubleTapped) - a modal
    // dialog with no default-focused control otherwise leaves keyboard users with no
    // discoverable way to dismiss it besides Alt+F4; Escape is the standard, expected way out.
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
