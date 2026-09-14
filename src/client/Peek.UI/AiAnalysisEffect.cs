using Avalonia;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Peek.Core.Abstractions;
using Peek.UI.Views;
using Pipboy.Avalonia;
using System;
using System.Drawing;

namespace Peek.UI;

/// <summary>
/// <see cref="IAiAnalysisEffect"/> on top of <see cref="AiAnalysisWindow"/>.
/// </summary>
/// <remarks>
/// Deliberately a peer of HighlightOverlay rather than part of it: same shape of job, entirely
/// separate lifetime. See <see cref="IAiAnalysisEffect"/> for why they were split.
/// </remarks>
public sealed class AiAnalysisEffect : IAiAnalysisEffect, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly ILogger<AiAnalysisEffect> _logger;
    private readonly PipboyThemeManager _themeManager;

    private AiAnalysisWindow? _window;

    public AiAnalysisEffect(PipboyThemeManager themeManager, ILogger<AiAnalysisEffect> logger)
    {
        _dispatcher = Application.Current!.Dispatcher;
        _logger = logger;
        _themeManager = themeManager;
        _themeManager.ThemeColorChanged += OnThemeColorChanged;
    }

    private void OnThemeColorChanged(object? sender, ThemeColorChangedEventArgs e)
    {
        if (_window is null) return;
        _dispatcher.Invoke(() => _window?.ResetEffectColor(e.Palette.Primary));
    }

    public void Show(Rectangle rect)
    {
        if (rect.Width < AiAnalysisWindow.MinWindowSize || rect.Height < AiAnalysisWindow.MinWindowSize)
        {
            _logger.LogDebug("Target is {Width}x{Height} - too small for the AI analysis effect, skipping it",
                rect.Width, rect.Height);
            return;
        }

        _dispatcher.Invoke(() =>
        {
            _window ??= new AiAnalysisWindow();

            var scaling = _window.Screens.Primary?.Scaling ?? 1.0;
            _window.Position = new PixelPoint((int)(rect.X / scaling), (int)(rect.Y / scaling));
            _window.Width = rect.Width / scaling;
            _window.Height = rect.Height / scaling;

            _window.Show();
        });

        _logger.LogDebug("AI analysis effect shown over {Width}x{Height} at {X},{Y}",
            rect.Width, rect.Height, rect.X, rect.Y);
    }

    public void Hide()
    {
        if (_window is null) return;

        _dispatcher.Invoke(() => _window?.Hide());
        _logger.LogDebug("AI analysis effect hidden");
    }

    public void Dispose()
    {
        _themeManager.ThemeColorChanged -= OnThemeColorChanged;

        _dispatcher.Invoke(() =>
        {
            _window?.Close();
            _window = null;
        });
    }
}
