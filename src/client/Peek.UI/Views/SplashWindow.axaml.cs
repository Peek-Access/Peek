using AsyncNavigation.Abstractions;
using Avalonia.Controls;

namespace Peek.UI.Views;

public partial class SplashWindow : Window, IView, IDialogWindow
{
    public SplashWindow()
    {
        InitializeComponent();
    }
    
}