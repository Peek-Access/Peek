using Peek.Worker.Contracts.Automation;
using Peek.Core.Services.Llm;
using Xunit;

namespace Peek.Core.Tests.Llm;

public class ContextAggregatorTests
{
    private readonly ContextAggregator _aggregator = new();

    [Fact]
    public void Builds_system_context_and_command_messages_in_order()
    {
        var element = new SemanticElement { Name = "Login", ControlType = "Button", IsEnabled = true };

        var messages = _aggregator.BuildMessages(new LlmContext
        {
            FocusedElement = element,
            OcrText = "Welcome back\nUsername\nPassword",
            UserCommand = "What is this screen for?",
            UserSystemPromptCustomization = "Prefer UK English spelling.",
        });

        Assert.Equal(3, messages.Count);

        Assert.Equal("system", messages[0].Role);
        Assert.Contains(AccessibilityPromptBuilder.BasePrompt, messages[0].Content);
        Assert.Contains("UK English", messages[0].Content);

        Assert.Equal("user", messages[1].Role);
        Assert.Contains("Login", messages[1].Content);
        Assert.Contains("Button", messages[1].Content);
        Assert.Contains("Welcome back", messages[1].Content);

        Assert.Equal("user", messages[2].Role);
        Assert.Equal("What is this screen for?", messages[2].Content);
    }

    [Fact]
    public void Omits_the_context_message_when_there_is_no_UIA_or_OCR_data()
    {
        var messages = _aggregator.BuildMessages(new LlmContext
        {
            UserCommand = "Describe the window.",
        });

        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal("user", messages[1].Role);
        Assert.Equal("Describe the window.", messages[1].Content);
    }

    [Fact]
    public void Base_prompt_is_used_verbatim_when_there_is_no_user_customization()
    {
        var messages = _aggregator.BuildMessages(new LlmContext { UserCommand = "x" });

        Assert.Equal(AccessibilityPromptBuilder.BasePrompt, messages[0].Content);
    }
}
