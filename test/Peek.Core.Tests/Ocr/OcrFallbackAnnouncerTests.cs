using Peek.Core.Services.Ocr;
using Peek.Worker.Contracts.Automation;
using Xunit;

namespace Peek.Core.Tests.Ocr;

/// <summary>
/// Covers the pure gating logic OcrFallbackAnnouncer.TryAnnounceAsync checks before ever
/// touching a screenshot/OCR call - the two conditions are exposed as public static methods
/// specifically so they're testable without a WorkerConnection or WindowEnumerator (see
/// ElementTrackerHoverTests for why those need a real worker process to exercise at all).
/// </summary>
public class OcrFallbackAnnouncerTests
{
    [Fact]
    public void Element_with_a_name_has_meaningful_content()
    {
        var element = new SemanticElement { Name = "Save" };

        Assert.True(OcrFallbackAnnouncer.HasMeaningfulContent(element));
    }

    [Fact]
    public void Element_with_only_a_value_has_meaningful_content()
    {
        var element = new SemanticElement { Name = "", Value = "42%" };

        Assert.True(OcrFallbackAnnouncer.HasMeaningfulContent(element));
    }

    [Fact]
    public void Element_with_neither_name_nor_value_has_no_meaningful_content()
    {
        var element = new SemanticElement { Name = "", Value = null };

        Assert.False(OcrFallbackAnnouncer.HasMeaningfulContent(element));
    }

    [Fact]
    public void A_top_level_window_s_own_title_does_not_count_as_meaningful_content()
    {
        // The exact WeChat/Weixin scenario this class exists for: every hover position
        // resolves to the same Window element, whose Name is just the app's window title -
        // every real window has one, so counting it would make this always "meaningful" for
        // precisely the case that needs to be detected as opaque instead.
        var element = new SemanticElement { Name = "Weixin", ControlType = "Window" };

        Assert.False(OcrFallbackAnnouncer.HasMeaningfulContent(element));
    }

    [Fact]
    public void A_non_window_element_with_a_name_still_counts_as_meaningful()
    {
        var element = new SemanticElement { Name = "Save", ControlType = "Button" };

        Assert.True(OcrFallbackAnnouncer.HasMeaningfulContent(element));
    }

    [Fact]
    public void Whitespace_only_name_and_value_do_not_count_as_meaningful()
    {
        var element = new SemanticElement { Name = "   ", Value = "\t" };

        Assert.False(OcrFallbackAnnouncer.HasMeaningfulContent(element));
    }

    [Theory]
    [InlineData("Weixin")]
    [InlineData("weixin")]
    [InlineData("WEIXIN")]
    [InlineData("WeChat")]
    public void Known_problematic_process_names_match_case_insensitively(string processName)
    {
        var known = new[] { "Weixin", "WeChat" };

        Assert.True(OcrFallbackAnnouncer.IsKnownProblematicApplication(processName, known));
    }

    [Fact]
    public void Unlisted_process_is_not_known_problematic()
    {
        var known = new[] { "Weixin", "WeChat" };

        Assert.False(OcrFallbackAnnouncer.IsKnownProblematicApplication("notepad", known));
    }

    [Fact]
    public void Empty_process_name_is_never_known_problematic()
    {
        Assert.False(OcrFallbackAnnouncer.IsKnownProblematicApplication("", ["Weixin"]));
    }

    // These pin down a real bug found while integration-testing against a window with zero
    // content controls (see ElementTrackerHoverTests.Hovering_a_known_problematic_UIA_opaque_
    // window_reads_its_content_via_OCR): automation.getChildren still returns the window's
    // title bar, system menu, and minimize/maximize/close buttons - every real window has
    // these, all with non-empty names - so without this exclusion, IsWindowUiaOpaqueAsync's
    // "does this window have any meaningful descendant" probe judged *every* window non-opaque,
    // chrome included, regardless of whether the app exposed anything past it.

    [Theory]
    [InlineData("TitleBar", "Untestable Chat App")]
    [InlineData("MenuBar", "System Menu Bar")]
    [InlineData("MenuItem", "System")]
    [InlineData("Button", "Minimize")]
    [InlineData("Button", "Maximize")]
    [InlineData("Button", "Close")]
    [InlineData("Button", "Restore")]
    public void Standard_window_chrome_elements_are_recognized(string controlType, string name)
    {
        var element = new SemanticElement { ControlType = controlType, Name = name };

        Assert.True(OcrFallbackAnnouncer.IsStandardWindowChrome(element));
    }

    [Fact]
    public void A_content_button_is_not_window_chrome()
    {
        var element = new SemanticElement { ControlType = "Button", Name = "Save" };

        Assert.False(OcrFallbackAnnouncer.IsStandardWindowChrome(element));
    }

    [Fact]
    public void The_context_help_title_bar_button_is_window_chrome()
    {
        // Found against a real Weixin window (AutomationId "Help", English display name
        // "Context help") - the WS_EX_CONTEXTHELP "?" title-bar button, same category as
        // Minimize/Maximize/Close but keyed by AutomationId since its Name isn't one of those.
        var element = new SemanticElement { ControlType = "Button", Name = "Context help", AutomationId = "Help" };

        Assert.True(OcrFallbackAnnouncer.IsStandardWindowChrome(element));
    }

    [Fact]
    public void A_menu_item_that_is_not_the_system_menu_is_not_window_chrome()
    {
        var element = new SemanticElement { ControlType = "MenuItem", Name = "File" };

        Assert.False(OcrFallbackAnnouncer.IsStandardWindowChrome(element));
    }

    // Found against a real Weixin window: automation.getChildren returned a "Pane" named
    // "MMUIRenderSubWindowHW" (an internal rendering-surface class name) and another "Pane"
    // whose Name just duplicated the window's own title ("Weixin") - neither is content a user
    // would want read, but both have a non-empty Name, so HasMeaningfulContent alone judged the
    // window non-opaque. Real readable content lives on leaf controls (Text, Edit, Button,
    // ListItem, ...), never on a pure layout container - regardless of what Name ends up on it.

    [Theory]
    [InlineData("Pane")]
    [InlineData("Group")]
    [InlineData("Custom")]
    [InlineData("Window")]
    [InlineData("ToolBar")]
    [InlineData("ScrollBar")]
    [InlineData("List")]
    [InlineData("Tree")]
    [InlineData("Table")]
    public void Structural_container_control_types_are_recognized(string controlType)
    {
        Assert.True(OcrFallbackAnnouncer.IsStructuralContainer(controlType));
    }

    [Theory]
    [InlineData("Text")]
    [InlineData("Edit")]
    [InlineData("Button")]
    [InlineData("ListItem")]
    [InlineData("Document")]
    [InlineData("Hyperlink")]
    public void Leaf_content_control_types_are_not_structural_containers(string controlType)
    {
        Assert.False(OcrFallbackAnnouncer.IsStructuralContainer(controlType));
    }

    [Fact]
    public void Extracting_message_names_the_window()
    {
        Assert.Equal("Extracting text from Weixin, one moment...", OcrFallbackAnnouncer.FormatExtractingMessage("Weixin"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Extracting_message_falls_back_to_a_generic_phrasing_when_the_window_has_no_name(string? windowName)
    {
        Assert.Equal("Extracting text, one moment...", OcrFallbackAnnouncer.FormatExtractingMessage(windowName));
    }
}
