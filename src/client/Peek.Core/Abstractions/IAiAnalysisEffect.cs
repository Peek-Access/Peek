using System.Drawing;

namespace Peek.Core.Abstractions;

/// <summary>
/// The visual "the AI is looking at this window" effect - a CRT scan/noise overlay drawn over
/// the window being analysed.
/// </summary>
/// <remarks>
/// Its own service and its own window, deliberately: it must be reasoned about - and switched
/// off - independently of <see cref="IHighlightService"/>. They answer different questions:
/// the highlight says "this is the element the screen reader is on", this says "a long
/// operation you started is running on this window". Nothing here touches the highlight, and
/// nothing in the highlight knows this exists.
/// </remarks>
public interface IAiAnalysisEffect
{
    /// <summary>
    /// Shows the effect over <paramref name="rect"/> (screen coordinates). Skipped for a
    /// target too small to render it legibly - see the implementation's minimum size.
    /// </summary>
    void Show(Rectangle rect);

    /// <summary>Hides the effect. Safe to call when it isn't showing.</summary>
    void Hide();
}
