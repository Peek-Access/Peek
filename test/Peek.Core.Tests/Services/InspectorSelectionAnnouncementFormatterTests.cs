using Peek.Core.Services;
using Xunit;

namespace Peek.Core.Tests.Services;

public sealed class InspectorSelectionAnnouncementFormatterTests
{
    [Fact]
    public void Formats_name_type_process_and_live_position_into_one_sentence()
    {
        var text = InspectorSelectionAnnouncementFormatter.Format("Save", "Button", "Notepad", 120, 340, 80, 24, Invariant);

        Assert.Equal("Save, Button, in Notepad, at 120, 340, size 80 by 24.", text);
    }

    [Fact]
    public void Omits_the_process_clause_when_no_process_name_is_available()
    {
        var text = InspectorSelectionAnnouncementFormatter.Format("Save", "Button", null, 120, 340, 80, 24, Invariant);

        Assert.Equal("Save, Button, at 120, 340, size 80 by 24.", text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Falls_back_to_Unnamed_for_a_blank_name_rather_than_an_empty_leading_comma(string? name)
    {
        var text = InspectorSelectionAnnouncementFormatter.Format(name!, "Button", "Notepad", 0, 0, 10, 10, Invariant);

        Assert.StartsWith("Unnamed, Button", text);
    }
}
