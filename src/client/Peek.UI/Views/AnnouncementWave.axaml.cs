using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Diagnostics;

namespace Peek.UI.Views;

/// <summary>Fixed-layout, HTML-demo-compatible voice waveform.</summary>
public partial class AnnouncementWave : UserControl
{
    public static readonly StyledProperty<bool> IsSpeakingProperty =
        AvaloniaProperty.Register<AnnouncementWave, bool>(nameof(IsSpeaking));

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly double[] _levels = new double[17];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _previousSeconds;
    private double _time;

    public bool IsSpeaking
    {
        get => GetValue(IsSpeakingProperty);
        set => SetValue(IsSpeakingProperty, value);
    }

    public AnnouncementWave()
    {
        InitializeComponent();
        Array.Fill(_levels, 0.08);
        _timer.Tick += (_, _) => Advance();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsSpeakingProperty) return;

        // Keep the idle state completely still. Starting the timer only when speech begins
        // also lets a stop event animate the bars back to their resting dots before idling.
        _timer.Start();
    }

    private void Advance()
    {
        var seconds = _clock.Elapsed.TotalSeconds;
        var dt = Math.Min(Math.Max(seconds - _previousSeconds, 0), 0.05);
        _previousSeconds = seconds;
        if (IsSpeaking) _time += dt;

        var settling = false;
        for (var i = 0; i < _levels.Length; i++)
        {
            var target = IsSpeaking ? SpeakingLevel(i) : 0.08;
            _levels[i] += (target - _levels[i]) * (1 - Math.Exp(-dt * 16));
            settling |= Math.Abs(_levels[i] - target) > 0.001;
        }

        InvalidateVisual();
        if (!IsSpeaking && !settling)
            _timer.Stop();
    }

    private double SpeakingLevel(int index)
    {
        var shape = Math.Pow(Math.Sin(Math.PI * (index + 1) / 18), 1.2);
        var pulse = 0.24 + 0.48 * Math.Pow(Math.Sin(_time * 7.8 - index * 0.67), 2)
            + 0.28 * Math.Pow(Math.Sin(_time * 12.1 + index * 1.7), 2);
        var breath = 0.6 + 0.4 * Math.Pow(Math.Sin(_time * 2.6), 2);
        return 0.08 + shape * pulse * breath * 0.92;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var brush = this.TryFindResource("PipboyPrimaryBrush", out var resource) && resource is IBrush found
            ? found
            : Brushes.LimeGreen;
        var lineBrush = this.TryFindResource("PipboyBorderBrush", out var line) && line is IBrush border
            ? border
            : Brushes.DarkGreen;
        var barWidth = 4d;
        var gap = 4d;
        var barsWidth = _levels.Length * barWidth + (_levels.Length - 1) * gap;
        var startX = Math.Max(0, (Bounds.Width - barsWidth) / 2);
        var x = startX;
        var center = Bounds.Height / 2;

        // The HTML demo places the active waveform in the middle of a quiet baseline.
        // Keep a small breathing gap around the bars so the two side lines read as one
        // continuous guide without touching the animated columns.
        var linePen = new Pen(lineBrush, 1);
        if (startX > 8)
            context.DrawLine(linePen, new Point(0, center), new Point(startX - 8, center));

        for (var i = 0; i < _levels.Length; i++)
        {
            var height = 64 * _levels[i];
            context.DrawRectangle(
                brush,
                null,
                new Rect(x, center - height / 2, barWidth, height),
                2,
                2,
                default);
            x += barWidth + gap;
        }

        var endX = startX + barsWidth;
        if (Bounds.Width > endX + 8)
            context.DrawLine(linePen, new Point(endX + 8, center), new Point(Bounds.Width, center));
    }
}
