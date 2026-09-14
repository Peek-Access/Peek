using Peek.Core.Services;
using Xunit;

namespace Peek.Core.Tests.Services;

/// <summary>
/// Which top-level windows the Inspector's list is willing to show.
/// </summary>
/// <remarks>
/// Grounded in a measurement of a real desktop rather than a guess about what "noise" means.
/// A minimum size alone would miss the worst offenders: two DWM-cloaked windows (Settings and
/// Windows Input Experience) sit in the list at 1920x1032 and 1920x1080, reported visible by
/// <c>IsWindowVisible</c>, while not being on screen at all. Only one genuinely tiny window
/// exists, a 12x37 cursor helper.
/// <para>
/// Size alone is also not sufficient to exclude: 160x28 entries that look like noise are
/// minimised windows with real titles, and selecting one is how a user restores it.
/// </para>
/// </remarks>
public sealed class WindowListFilterTests
{
    [Fact]
    public void An_ordinary_window_is_listed()
    {
        Assert.True(WindowEnumerator.ShouldList(isMinimized: false, width: 1200, height: 800, isCloaked: false));
    }

    [Fact]
    public void A_cloaked_window_is_hidden_however_large_it_is()
    {
        // The case a size threshold can never catch, and the one that actually mattered: a
        // suspended UWP app is full-screen, "visible", and not on screen.
        Assert.False(WindowEnumerator.ShouldList(isMinimized: false, width: 1920, height: 1080, isCloaked: true));
    }

    [Fact]
    public void A_tiny_helper_window_is_hidden()
    {
        // Measured: a 12x37 unnamed "CursorVisualClass" window.
        Assert.False(WindowEnumerator.ShouldList(isMinimized: false, width: 12, height: 37, isCloaked: false));
    }

    [Fact]
    public void A_window_tiny_in_only_one_dimension_is_hidden()
    {
        Assert.False(WindowEnumerator.ShouldList(isMinimized: false, width: 1920, height: 8, isCloaked: false));
        Assert.False(WindowEnumerator.ShouldList(isMinimized: false, width: 8, height: 1080, isCloaked: false));
    }

    [Fact]
    public void The_taskbar_is_listed()
    {
        // 1920x48 on the measured desktop - real, visible, and worth being able to inspect.
        // This is why the threshold is 32 and not a rounder-looking 64.
        Assert.True(WindowEnumerator.ShouldList(isMinimized: false, width: 1920, height: 48, isCloaked: false));
        Assert.True(WindowEnumerator.MinimumPointableSize < 48);
    }

    [Fact]
    public void A_minimized_window_is_listed_despite_its_stub_rectangle()
    {
        // Minimised windows report a meaningless 160x28. They are real windows with real
        // titles, and selecting one in the Inspector is how a user restores it - filtering
        // them out by size would have quietly removed that.
        Assert.True(WindowEnumerator.ShouldList(isMinimized: true, width: 160, height: 28, isCloaked: false));
    }

    [Fact]
    public void A_minimized_window_that_is_also_cloaked_is_still_hidden()
    {
        // Cloak wins: the exemption is for "its rectangle lies", not "show it regardless".
        Assert.False(WindowEnumerator.ShouldList(isMinimized: true, width: 160, height: 28, isCloaked: true));
    }

    [Fact]
    public void The_threshold_is_inclusive()
    {
        var min = WindowEnumerator.MinimumPointableSize;

        Assert.True(WindowEnumerator.ShouldList(false, min, min, false));
        Assert.False(WindowEnumerator.ShouldList(false, min - 1, min, false));
        Assert.False(WindowEnumerator.ShouldList(false, min, min - 1, false));
    }
}
