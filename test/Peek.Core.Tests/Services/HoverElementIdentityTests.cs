using Peek.Core.Services;
using Peek.Worker.Contracts.Automation;
using Xunit;

namespace Peek.Core.Tests.Services;

/// <summary>
/// The hover announcement pipeline only speaks when the element under the cursor actually
/// changes. These pin what "actually changes" means.
/// </summary>
/// <remarks>
/// <see cref="SemanticElement"/> is a class with no equality of its own and the worker returns
/// a freshly deserialized instance per query, so the default reference comparison answered
/// "different" every single time. Drifting the cursor within one large control therefore
/// re-announced it repeatedly, each announcement cutting off the previous one mid-word - the
/// reported "speech is chaotic when the mouse moves fast".
/// </remarks>
public sealed class HoverElementIdentityTests
{
    [Fact]
    public void Two_reads_of_the_same_control_are_the_same_element()
    {
        // The defect in one assertion: without this, the pipeline speaks twice.
        Assert.True(ElementTracker.HoverIdentityComparer.Equals(Button(), Button()));
    }

    [Fact]
    public void Moving_to_a_different_control_is_a_change()
    {
        var other = Button("Cancel");

        Assert.False(ElementTracker.HoverIdentityComparer.Equals(Button(), other));
    }

    [Fact]
    public void Two_rows_that_share_a_name_are_still_different_elements()
    {
        // List rows routinely share a name and control type; only position separates them, so
        // position has to be part of the identity or moving between rows stays silent.
        var first = Button();
        var second = Button(top: 240);

        Assert.False(ElementTracker.HoverIdentityComparer.Equals(first, second));
    }

    [Fact]
    public void The_workers_element_id_wins_when_present()
    {
        // Same id, everything else different: the worker already told us it's the same element.
        var a = new SemanticElement { ElementId = "abc", Name = "Save", Rect = new SemanticRect { Left = 10 } };
        var b = new SemanticElement { ElementId = "abc", Name = "Something else", Rect = new SemanticRect { Left = 900 } };

        Assert.True(ElementTracker.HoverIdentityComparer.Equals(a, b));
    }

    [Fact]
    public void A_different_element_id_is_a_change_even_if_everything_else_matches()
    {
        var a = new SemanticElement { ElementId = "abc", Name = "Save" };
        var b = new SemanticElement { ElementId = "xyz", Name = "Save" };

        Assert.False(ElementTracker.HoverIdentityComparer.Equals(a, b));
    }

    [Fact]
    public void Equal_elements_hash_equally()
    {
        Assert.Equal(
            ElementTracker.HoverIdentityComparer.GetHashCode(Button()),
            ElementTracker.HoverIdentityComparer.GetHashCode(Button()));
    }

    [Fact]
    public void Null_handling_does_not_throw()
    {
        Assert.True(ElementTracker.HoverIdentityComparer.Equals(null, null));
        Assert.False(ElementTracker.HoverIdentityComparer.Equals(Button(), null));
        Assert.False(ElementTracker.HoverIdentityComparer.Equals(null, Button()));
    }

    private static SemanticElement Button(string name = "Save", int top = 200) => new()
    {
        Name = name,
        ControlType = "Button",
        AutomationId = "saveBtn",
        Hwnd = 0x1234,
        Rect = new SemanticRect { Left = 100, Top = top, Width = 80, Height = 24 },
    };
}
