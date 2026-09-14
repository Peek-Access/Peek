using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Pipboy.Avalonia;
using Pipboy.Avalonia.Fx.Controls;
using ReactiveUI;
using System;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace Peek.UI.Views;

public class HighlightBorder : Window, IDisposable
{
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const uint WS_POPUP = 0x80000000u;

    private const double BeamMinWindowSize = 10.0;
    private const double BeamMaxWindowSize = 600.0;
    private const double BeamMinWidth = 10.0;
    private const double BeamMaxWidth = 120.0;

    public double BeamSpeed { get; set; } = 0.0028;

    private readonly Border _outerBorder;
    private readonly Border _innerBorder;
    private readonly Panel _effectsLayer;
    private readonly Border _breathEffectBorder;
    private readonly ProgressBar _loadingControl;
    private readonly Canvas _beamCanvas;
    private readonly Border _movingBeam;

    private readonly RotateTransform _beamRotation = new();
    private IDisposable? _beamAnimationDisposable;
    private double _beamProgress;
    private CancellationTokenSource? _breathCts;
    private bool _disposed;

    // IterationCount.Infinite here specifically, not just "a big number": Avalonia's
    // Animation.RunAsync throws InvalidOperationException("Looping animations must not
    // use the Run method.") for an Infinite animation - its Task is meant to complete
    // when the animation finishes, which an infinite one never does. Two iterations
    // (PlaybackDirection.Alternate reverses the second one) is one full breathe-in/
    // breathe-out pulse; StartBreathAnimation loops this call instead of running it once.
    private static readonly Animation _breathAnimation = new()
    {
        Duration = TimeSpan.FromMilliseconds(500),
        IterationCount = new IterationCount(2),
        PlaybackDirection = PlaybackDirection.Alternate,
        Children =
        {
            new KeyFrame
            {
                Cue = new Cue(0d),
                Setters =
                {
                    new Setter(OpacityProperty, 0.8d),
                    new Setter(Border.BorderThicknessProperty, new Thickness(5))
                }
            },
            new KeyFrame
            {
                Cue = new Cue(1d),
                Setters =
                {
                    new Setter(OpacityProperty, 0.2d),
                    new Setter(Border.BorderThicknessProperty, new Thickness(1))
                }
            }
        }
    };

    public HighlightBorder()
    {
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        IsHitTestVisible = false;
        IsEnabled = false;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ExtendClientAreaTitleBarHeightHint = -1;

        Win32Properties.AddWindowStylesCallback(this, (style, exStyle) =>
            (style | WS_POPUP, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED));

        _innerBorder = new Border
        {
            BorderThickness = new Thickness(2.0),
            CornerRadius = new CornerRadius(0.0),
            Background = new SolidColorBrush(PipboyThemeManager.Instance.PrimaryColor.WithOpacity(0.2)),
        };
        _innerBorder.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("PipboyPrimaryBrush"));

        _outerBorder = new Border
        {
            BorderThickness = new Thickness(1.0),
            BorderBrush = Brushes.Black,
            CornerRadius = new CornerRadius(0.0),
            Child = _innerBorder
        };

        _effectsLayer = new Panel { IsHitTestVisible = false };
        _effectsLayer.Children.Add(_outerBorder);

        _loadingControl = new ProgressBar
        {
            Width = 24,
            Height = 24,
            IsIndeterminate = true,
            IsVisible = false,
            IsHitTestVisible = false,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        _loadingControl.Bind(ForegroundProperty, new DynamicResourceExtension("PipboyPrimaryBrush"));

        _breathEffectBorder = new Border
        {
            BorderThickness = new Thickness(4),
            IsVisible = false,
            IsHitTestVisible = false,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            CornerRadius = new CornerRadius(2)
        };
        _breathEffectBorder.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("PipboyPrimaryBrush"));

        _effectsLayer.Children.Add(_loadingControl);
        _effectsLayer.Children.Add(_breathEffectBorder);


        _movingBeam = new Border
        {
            Width = 72,
            Height = 4,
            IsVisible = false,
            CornerRadius = new CornerRadius(0),
            RenderTransformOrigin = RelativePoint.Center,
            // One transform instance, mutated per frame - see UpdateBeamPosition.
            RenderTransform = _beamRotation,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Colors.Transparent, 0.0),
                    new GradientStop(PipboyThemeManager.Instance.PrimaryColor.WithOpacity(0.25), 0.35),
                    new GradientStop(PipboyThemeManager.Instance.PrimaryColor, 0.75),
                    new GradientStop(Colors.White.WithOpacity(0.9), 1.0)
                }
            },
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = 1,
                Spread = 2,
                OffsetX = 0,
                OffsetY = 0,
                Color = PipboyThemeManager.Instance.PrimaryColor
            }),
        };

        _beamCanvas = new Canvas { IsHitTestVisible = false };
        _beamCanvas.Children.Add(_movingBeam);
        _effectsLayer.Children.Add(_beamCanvas);

        Content = _effectsLayer;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        TryStartBeamAnimation();
    }

    public void StartLoading()
    {
        StopLoading();
        _loadingControl.IsVisible = true;
    }
    public void StopLoading()
    {
        _loadingControl.IsVisible = false;
    }

    public async Task StartBreathAnimationAsync(CancellationToken externalToken = default)
    {
        await StopBreathAnimationAsync();

        _breathCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        var token = _breathCts.Token;

        StartBreathAnimation(token);
    }


    private async void StartBreathAnimation(CancellationToken cancellationToken)
    {
        _breathEffectBorder.IsVisible = true;
        _breathEffectBorder.Opacity = 0.8;
        _breathEffectBorder.BorderThickness = new Thickness(5);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _breathAnimation.RunAsync(_breathEffectBorder, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }


    public async Task StopBreathAnimationAsync()
    {
        if (_breathCts != null)
        {
            await Application.Current!.Dispatcher.InvokeAsync(() =>
            {
                _breathCts.Cancel();
            });
            _breathCts?.Dispose();
            _breathCts = null;
        }

        _breathEffectBorder.IsVisible = false;
    }
    private void StopBeamAnimation()
    {
        _beamAnimationDisposable?.Dispose();
        _beamAnimationDisposable = null;
        _movingBeam.IsVisible = false;
    }

    public void ResetFillBrush(SolidColorBrush brush)
    {
        _innerBorder.Background = brush;
    }

    public void ResetBorderBrush(SolidColorBrush brush)
    {
        _innerBorder.BorderBrush = brush; 
    }

    public void ResetBeamEffectColor(Color color)
    {
        _movingBeam.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops =
                {
                    new GradientStop(Colors.Transparent, 0.0),
                    new GradientStop(color.WithOpacity(0.25), 0.35),
                    new GradientStop(color, 0.75),
                    new GradientStop(Colors.White.WithOpacity(0.9), 1.0)
                }
        };
        _movingBeam.BoxShadow = new BoxShadows(new BoxShadow
        {
            Blur = 1,
            Spread = 2,
            OffsetX = 0,
            OffsetY = 0,
            Color = color
        });
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    private void TryStartBeamAnimation()
    {
        var perimeter = Bounds.Width * 2 + Bounds.Height * 2;
        if (perimeter > 0 && !IsWindowTooSmallForBeam())
        {
            _beamProgress = 0;
            UpdateBeamSize();
            _movingBeam.IsVisible = true;

            _beamAnimationDisposable =
                Signal
                    .Interval(TimeSpan.FromMilliseconds(12))
                    //.TakeUntil(cancellationToken)
                    .ObserveOn(RxSchedulers.MainThreadScheduler)
                    .Subscribe(time =>
                    {
                        _beamProgress += BeamSpeed;
                        if (_beamProgress >= 1)
                            _beamProgress = 0;

                        UpdateBeamSize();
                        UpdateBeamPosition(_beamProgress);
                    });
        }
    }
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            _breathCts?.Cancel();
            _breathCts?.Dispose();
            _beamAnimationDisposable?.Dispose();
        }
        _disposed = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    private bool IsWindowTooSmallForBeam()
    {
        return Bounds.Width < BeamMinWindowSize || Bounds.Height < BeamMinWindowSize;
    }
    private void UpdateBeamSize()
    {
        if (IsWindowTooSmallForBeam())
        {
            _movingBeam.IsVisible = false;
            return;
        }

        var dominant = Math.Max(Bounds.Width, Bounds.Height);
        var t = Math.Clamp((dominant - BeamMinWindowSize) / (BeamMaxWindowSize - BeamMinWindowSize), 0.0, 1.0);
        _movingBeam.Width = BeamMinWidth + t * (BeamMaxWidth - BeamMinWidth);
    }
    private void UpdateBeamPosition(double progress)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;

        if (width <= 0 || height <= 0)
            return;

        var perimeter = width * 2 + height * 2;
        var distance = progress * perimeter;

        double x, y, angle;

        if (distance < width)
        {
            x = distance;
            y = 0;
            angle = 0;
        }
        else if (distance < width + height)
        {
            x = width;
            y = distance - width;
            angle = 90;
        }
        else if (distance < width * 2 + height)
        {
            x = width - (distance - width - height);
            y = height;
            angle = 180;
        }
        else
        {
            x = 0;
            y = height - (distance - width * 2 - height);
            angle = 270;
        }

        Canvas.SetLeft(_movingBeam, x - _movingBeam.Width / 2);
        Canvas.SetTop(_movingBeam, y - _movingBeam.Height / 2);

        // Mutate the existing transform rather than assigning a new one. This runs ~83
        // times a second for as long as a highlight is on screen, and assigning
        // RenderTransform doesn't just allocate - it re-runs Avalonia's property-change
        // and transform-invalidation path every frame. The angle only ever takes four
        // values, so there was never anything to allocate for in the first place.
        _beamRotation.Angle = angle;
    }
}