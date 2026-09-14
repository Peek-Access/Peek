using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Peek.Core.Abstractions;
using Peek.UI.Views;
using Pipboy.Avalonia;
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace Peek.UI;

public class HighlightOverlay : IHighlightOverlay
{
    private readonly Dispatcher _dispatcher;
    private HighlightBorder? _window;
    private readonly ILogger _logger;
    private readonly PipboyThemeManager _pipboyThemeManager;

    public HighlightOverlay(PipboyThemeManager pipboyThemeManager,
        ILogger<HighlightOverlay> logger)
    {
        _dispatcher = Application.Current!.Dispatcher;
        _logger = logger;
        _pipboyThemeManager = pipboyThemeManager;
        _pipboyThemeManager.ThemeColorChanged += PipboyThemeManager_ThemeColorChanged;
    }

    private void PipboyThemeManager_ThemeColorChanged(object? sender, ThemeColorChangedEventArgs e)
    {
        SetFillColor(e.Palette.Primary.WithOpacity(0.2).ToString());
        SetBeamEffectColor(e.Palette.Primary.ToString());
        SetBorderColor(e.Palette.Primary.ToString());

    }

    public void Show(Rectangle rect)
    {
        _dispatcher.Invoke(() =>
        {
            if (_window == null)
            {
                _window = new HighlightBorder();
                _window.Show();
            }
            _window.Show();
            Reposition(rect);
        });
    }

    public void Hide()
    {
        _dispatcher.Invoke(() => _window?.Hide());
    }

    public void Close()
    {
        _dispatcher.Invoke(() =>
        {
            _window?.Close();
            _window = null;
        });
    }

    /// <summary>
    /// Moves the highlight. Posted rather than <c>Invoke</c>d: this is the one overlay call
    /// on a hot path - it fires for every element the mouse or keyboard lands on, from a
    /// background thread - and a blocking Invoke makes that caller wait on the UI thread,
    /// which is also the thread rendering the highlight's own animation. Nothing depends on
    /// the reposition having completed by the time this returns, and if several arrive in a
    /// row only the last position matters anyway.
    /// </summary>
    public void Update(Rectangle rect)
    {
        _dispatcher.Post(() => Reposition(rect));
    }

    public void Reset(Rectangle rect)
    {
        Update(rect);
    }

    public void SetBorderColor(string color)
    {
        if (_window == null) return;

        _dispatcher.Invoke(() =>
        {
            _window.ResetBorderBrush(new SolidColorBrush(Avalonia.Media.Color.Parse(color)));
        });
    }

    public void SetFillColor(string color)
    {
        if (_window == null) return;

        _dispatcher.Invoke(() =>
        {
            _window.ResetFillBrush(new SolidColorBrush(Avalonia.Media.Color.Parse(color)));
        });
    }
    public void SetBeamEffectColor(string color)
    {
        if (_window == null) return;

        _dispatcher.Invoke(() =>
        {
            _window.ResetBeamEffectColor(Avalonia.Media.Color.Parse(color));
        });
    }
    public nint GetNativeHandle()
    {
        var handle = _window?.TryGetPlatformHandle()?.Handle;
        return handle ?? throw new InvalidOperationException("No window handle");
    }

    private void Reposition(Rectangle rect)
    {
        if (_window == null) return;

        var scaling = _window.Screens.Primary?.Scaling ?? 1.0;

        _window.Position = new PixelPoint(
            (int)(rect.X / scaling),
            (int)(rect.Y / scaling));


        _window.Width = rect.Width / scaling;
        _window.Height = rect.Height / scaling;
    }

    public void Dispose()
    {
        _pipboyThemeManager.ThemeColorChanged -= PipboyThemeManager_ThemeColorChanged;
        Close();
    }

    /// <summary>
    /// Marshalled like every other method here. These two are called from the announcement
    /// pipeline, which runs entirely off the UI thread (deliberately - speech must not compete
    /// with rendering the highlight), and the animations underneath set Avalonia properties.
    /// Without the hop they throw "The calling thread cannot access this object because a
    /// different thread owns it" - and because that surfaced as an Rx OnError, it didn't just
    /// break one animation, it terminated the announcement stream for the rest of the session.
    /// </summary>
    public Task StartSpeakingAsync(CancellationToken cancellationToken) =>
        RunAnimationAsync(() => _window!.StartBreathAnimationAsync(cancellationToken));

    public Task StopSpeakingAsync() =>
        RunAnimationAsync(() => _window!.StopBreathAnimationAsync());

    /// <summary>
    /// Posts a breath animation to the UI thread and returns immediately, swallowing whatever
    /// it does. Deliberately not awaited on the caller's behalf: the caller is the announcement
    /// pipeline, and an announcement should neither wait for a decorative animation nor be
    /// killed by one.
    /// </summary>
    private Task RunAnimationAsync(Func<Task> animation)
    {
        if (_window is null) return Task.CompletedTask;

        _dispatcher.Post(async () =>
        {
            try
            {
                await animation();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Highlight breath animation failed");
            }
        });

        return Task.CompletedTask;
    }

}