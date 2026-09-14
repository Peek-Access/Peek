using Peek.Core.Services;
using Xunit;

namespace Peek.Core.Tests.Services;

public sealed class WindowAnnouncementFormatterTests
{
    [Theory]
    [InlineData(WindowAnnouncementKind.Opened, "New window, Notepad")]
    [InlineData(WindowAnnouncementKind.Closed, "Window closed, Notepad")]
    [InlineData(WindowAnnouncementKind.FocusChanged, "Active window, Notepad")]
    public void Formats_each_kind_with_a_distinct_prefix(WindowAnnouncementKind kind, string expected)
    {
        var text = WindowAnnouncementFormatter.Format(kind, "Notepad", Invariant);

        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Returns_null_for_a_blank_subject_rather_than_announcing_nothing_useful(string? subject)
    {
        var text = WindowAnnouncementFormatter.Format(WindowAnnouncementKind.Opened, subject, Invariant);

        Assert.Null(text);
    }
}
