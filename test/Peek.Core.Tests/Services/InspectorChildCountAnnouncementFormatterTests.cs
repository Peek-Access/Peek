using Peek.Core.Services;
using Xunit;

namespace Peek.Core.Tests.Services;

public sealed class InspectorChildCountAnnouncementFormatterTests
{
    [Fact]
    public void Zero_children_announces_none()
    {
        Assert.Equal("No child elements.", InspectorChildCountAnnouncementFormatter.Format(0, Invariant));
    }

    [Fact]
    public void One_child_uses_singular_wording()
    {
        Assert.Equal("1 child element.", InspectorChildCountAnnouncementFormatter.Format(1, Invariant));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(17)]
    public void Multiple_children_uses_plural_wording_with_the_count(int count)
    {
        Assert.Equal($"{count} child elements.", InspectorChildCountAnnouncementFormatter.Format(count, Invariant));
    }
}
