using Peek.Core.Services.Llm;
using Xunit;

namespace Peek.Core.Tests.Llm;

public class AccessibilityPromptBuilderTests
{
    [Fact]
    public void Null_customization_returns_the_base_prompt_unchanged()
    {
        Assert.Equal(AccessibilityPromptBuilder.BasePrompt, AccessibilityPromptBuilder.Build(null));
    }

    [Fact]
    public void Whitespace_customization_is_treated_as_absent()
    {
        Assert.Equal(AccessibilityPromptBuilder.BasePrompt, AccessibilityPromptBuilder.Build("   "));
    }

    [Fact]
    public void Customization_is_appended_after_the_base_prompt_not_instead_of_it()
    {
        var result = AccessibilityPromptBuilder.Build("Always mention colors.");

        Assert.StartsWith(AccessibilityPromptBuilder.BasePrompt, result);
        Assert.Contains("Always mention colors.", result);
    }
}
