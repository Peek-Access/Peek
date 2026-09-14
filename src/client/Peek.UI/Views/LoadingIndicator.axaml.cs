using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Peek.UI.Views;

public partial class LoadingIndicator : UserControl
{
    public LoadingIndicator()
    {
        InitializeComponent();
    }
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        MainCrt.EnableScanlines = true;
        MainCrt.ScanlineColor = Color.FromArgb(255, 0, 230, 60);
        MainCrt.ScanlineOpacity = 0.18;
        MainCrt.ScanlineSpacing = 3.0;
        MainCrt.EnableScanlineAnimation = true;
        MainCrt.ScanlineAnimSpeed = 20.0;
        MainCrt.EnableScanBeam = true;
        MainCrt.ScanBeamHeight = 60.0;
        MainCrt.EnableNoise = false;
        MainCrt.EnableVignette = true;
        MainCrt.VignetteIntensity = 0.55;
        MainCrt.EnableFlicker = false;
    }
}