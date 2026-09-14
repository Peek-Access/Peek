using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Pipboy.Avalonia;
using Pipboy.Avalonia.Fx.Controls;

namespace Peek.UI.Views;

/// <summary>
/// A transparent, click-through, always-on-top window that draws the "AI is analysing this"
/// CRT effect over the window being analysed.
/// </summary>
/// <remarks>
/// Split out of <see cref="HighlightBorder"/>, where this effect used to live as one more
/// layer inside the highlight box. Sharing a window meant sharing a lifetime and a position:
/// starting an analysis had to show the highlight in order to show the effect, so a feature
/// that has nothing to do with the screen reader ended up drawing the screen reader's box.
/// Separate windows let each appear on its own terms.
/// <para>
/// The effect itself is deliberately restrained. The first pass (scanlines at 0.35 opacity, a
/// fast bright scan beam, and flicker) looked harsh and, over a whole window rather than a
/// small element, covered the entire screenshot - "闪烁的效果太晃眼睛了…总之就是不好看".
/// Flicker is gone outright rather than toned down: it is a brightness-pulse effect, the
/// textbook eye-strain and photosensitivity trigger, and this app exists for
/// reading-impaired users. What remains is a slow, low-opacity ambient texture.
/// </para>
/// </remarks>
public class AiAnalysisWindow : Window
{
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const uint WS_POPUP = 0x80000000u;

    /// <summary>
    /// Below this size in either dimension the effect is not shown at all rather than shown
    /// cramped and illegible over a tiny target.
    /// </summary>
    public const double MinWindowSize = 60.0;

    private readonly CrtDisplay _effect;

    public AiAnalysisWindow()
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

        // Click-through and layered, like the highlight: this sits over another application's
        // window and must never take a click meant for it.
        Win32Properties.AddWindowStylesCallback(this, (style, exStyle) =>
            (style | WS_POPUP, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED));

        _effect = new CrtDisplay
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            EnableScanlines = true,
            ScanlineSpacing = 4,
            ScanlineHeight = 1,
            ScanlineOpacity = 0.08,
            EnableScanlineAnimation = true,
            ScanlineAnimSpeed = 10,
            EnableScanBeam = true,
            EnableScanBeamGradient = true,
            ScanBeamHeight = 5,
            ScanBeamSpeed = 28,
            EnableNoise = true,
            NoiseDensity = 0.04,
            NoiseOpacity = 0.04,
            NoisePixelSize = 2,
            NoiseRefreshIntervalMs = 250,
            // This covers a whole window, not a thin box - a vignette would read as a dark
            // smudge over real content.
            EnableVignette = false,
            EnableFlicker = false,
            ScanlineColor = PipboyThemeManager.Instance.PrimaryColor.WithOpacity(0.55),
            ScanBeamColor = PipboyThemeManager.Instance.PrimaryColor.WithOpacity(0.45),
        };

        Content = new Panel { IsHitTestVisible = false, Children = { _effect } };
    }

    /// <summary>Repaints the effect in the current theme colour - the palette can change while the window exists.</summary>
    public void ResetEffectColor(Color primary)
    {
        _effect.ScanlineColor = primary.WithOpacity(0.55);
        _effect.ScanBeamColor = primary.WithOpacity(0.45);
    }
}
