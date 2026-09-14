using Peek.Core.Services;
using Peek.Worker.Contracts.Automation;
using Xunit;

namespace Peek.Core.Tests.Services;

/// <summary>
/// What the focus highlight refuses to draw a box around.
/// </summary>
/// <remarks>
/// An unnamed, screen-filling Pane - e.g. the desktop behind Peek's docked panel - is a real
/// focused element, but outlining it conveys nothing while looking exactly like a bug; spoken,
/// it is equally empty: "pane".
/// <para>
/// The rule has to stay narrow, which is the point of these tests: it is the combination of
/// being anonymous AND covering the screen that makes an element worthless. A maximised window
/// with a real name is the same size and must still be announced.
/// </para>
/// </remarks>
public sealed class FocusHighlightFilterTests
{
    private const int ScreenWidth = 1920;
    private const int ScreenHeight = 1080;

    [Fact]
    public void An_unnamed_screen_filling_pane_is_skipped()
    {
        // The captured case, to the pixel: WhatsApp's root pane at 1500x1032.
        var element = Element(name: "", width: 1500, height: 1032);

        Assert.True(Filter(element));
    }

    [Fact]
    public void A_named_maximised_window_is_still_announced()
    {
        // The case that must not regress: same size, but it has a name, so it means something.
        var element = Element(name: "Notepad", width: 1920, height: 1080);

        Assert.False(Filter(element));
    }

    [Fact]
    public void A_small_unnamed_element_is_still_announced()
    {
        // Plenty of real controls have no name - an unlabelled icon button, say. Size is what
        // separates "unlabelled control" from "the entire screen".
        var element = Element(name: "", width: 32, height: 32);

        Assert.False(Filter(element));
    }

    [Fact]
    public void A_whitespace_name_counts_as_unnamed()
    {
        Assert.True(Filter(Element(name: "   ", width: 1900, height: 1000)));
    }

    [Fact]
    public void An_element_just_under_the_threshold_is_kept()
    {
        // 60% of the screen - a large panel, but not the screen. Still worth outlining.
        var element = Element(name: "", width: 1500, height: 830);

        Assert.False(Filter(element));
    }

    [Fact]
    public void Unknown_screen_metrics_never_suppress_anything()
    {
        // GetSystemMetrics can return 0 (session not attached to a desktop, RDP transitions).
        // Failing open matters here: silently dropping announcements is worse for this audience
        // than drawing one box too many.
        var element = Element(name: "", width: 1500, height: 1032);

        Assert.False(FocusAnnouncer.IsAnonymousFullScreenContainer(element, 0, 0));
    }

    private static bool Filter(SemanticElement element) =>
        FocusAnnouncer.IsAnonymousFullScreenContainer(element, ScreenWidth, ScreenHeight);

    private static SemanticElement Element(string name, int width, int height) => new()
    {
        Name = name,
        ControlType = "Pane",
        Rect = new SemanticRect { Left = 0, Top = 0, Width = width, Height = height },
    };
}
