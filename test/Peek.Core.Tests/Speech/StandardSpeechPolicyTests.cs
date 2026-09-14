using Peek.Worker.Contracts.Automation;
using Peek.Core.Services.Speech;
using Xunit;

namespace Peek.Core.Tests.Speech;

public class StandardSpeechPolicyTests
{
    private readonly StandardSpeechPolicy _policy = new();

    private static SemanticElement Button(string name = "Login", bool enabled = true) => new()
    {
        Name = name,
        ControlType = "Button",
        LocalizedControlType = "button",
        IsEnabled = enabled,
    };

    private static SemanticElement Checkbox(string name, ToggleState toggle) => new()
    {
        Name = name,
        ControlType = "CheckBox",
        LocalizedControlType = "checkbox",
        IsEnabled = true,
        ToggleState = toggle,
    };

    [Fact]
    public void Standard_speaks_name_and_role_for_a_button()
    {
        var content = _policy.Describe(Button("Login"), SpeechVerbosity.Standard);

        Assert.Equal("Login, button", content.Text);
    }

    [Fact]
    public void Standard_speaks_name_role_and_state_for_a_checked_checkbox()
    {
        var content = _policy.Describe(Checkbox("Remember me", ToggleState.On), SpeechVerbosity.Standard);

        Assert.Equal("Remember me, checkbox, checked", content.Text);
    }

    [Fact]
    public void Minimal_speaks_only_the_name()
    {
        var content = _policy.Describe(Checkbox("Remember me", ToggleState.On), SpeechVerbosity.Minimal);

        Assert.Equal("Remember me", content.Text);
    }

    [Fact]
    public void Detailed_adds_value_when_it_differs_from_the_name()
    {
        var element = new SemanticElement
        {
            Name = "Username",
            ControlType = "Edit",
            LocalizedControlType = "edit",
            IsEnabled = true,
            Value = "nevermore",
        };

        var content = _policy.Describe(element, SpeechVerbosity.Detailed);

        Assert.Contains("nevermore", content.Text);
    }

    [Fact]
    public void Detailed_does_not_repeat_a_value_equal_to_the_name()
    {
        var element = new SemanticElement
        {
            Name = "Submit",
            ControlType = "Button",
            LocalizedControlType = "button",
            IsEnabled = true,
            Value = "Submit",
        };

        var content = _policy.Describe(element, SpeechVerbosity.Detailed);

        // "Submit" should appear exactly once (the name), not duplicated as a value.
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(content.Text, "Submit").Count);
    }

    [Fact]
    public void Verbose_adds_description_on_top_of_detailed_fields()
    {
        var element = new SemanticElement
        {
            Name = "Search",
            ControlType = "Button",
            LocalizedControlType = "button",
            IsEnabled = true,
            Description = "Searches the current document",
        };

        var content = _policy.Describe(element, SpeechVerbosity.Verbose);

        Assert.Contains("Searches the current document", content.Text);
    }

    [Fact]
    public void Disabled_element_mentions_disabled_state_at_standard_verbosity()
    {
        var content = _policy.Describe(Button("Save", enabled: false), SpeechVerbosity.Standard);

        Assert.Contains("disabled", content.Text);
    }

    [Fact]
    public void Unlabeled_element_falls_back_to_a_placeholder()
    {
        var element = new SemanticElement { Name = "", ControlType = "Unknown", IsEnabled = true };

        var content = _policy.Describe(element, SpeechVerbosity.Minimal);

        Assert.Equal("Unlabeled element", content.Text);
    }
}
