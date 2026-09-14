using Peek.Worker.Contracts.Automation;
using Peek.Worker.Contracts.Llm;

namespace Peek.Core.Services.Llm;

public sealed class ContextAggregator : IContextAggregator
{
    public IReadOnlyList<LlmMessage> BuildMessages(LlmContext context)
    {
        var messages = new List<LlmMessage>
        {
            new() { Role = "system", Content = AccessibilityPromptBuilder.Build(context.UserSystemPromptCustomization) },
        };

        var facts = new List<string>();
        if (context.FocusedElement is { } element)
            facts.Add(DescribeElement(element));
        if (!string.IsNullOrWhiteSpace(context.OcrText))
            facts.Add($"Text recognized on screen via OCR (may contain recognition errors):\n{context.OcrText}");

        if (facts.Count > 0)
            messages.Add(new LlmMessage
            {
                Role = "user",
                Content = "Context (from UI Automation/OCR - treat as ground truth over your own guesses):\n" + string.Join("\n\n", facts),
            });

        messages.Add(new LlmMessage { Role = "user", Content = context.UserCommand });

        return messages;
    }

    private static string DescribeElement(SemanticElement element)
    {
        var parts = new List<string> { $"name=\"{element.Name}\"", $"role={element.ControlType}" };
        if (!string.IsNullOrEmpty(element.Value)) parts.Add($"value=\"{element.Value}\"");
        if (element.ToggleState is { } toggle) parts.Add($"toggle={toggle}");
        if (element.IsSelected is { } selected) parts.Add($"selected={selected}");
        if (!element.IsEnabled) parts.Add("disabled=true");
        return "Focused UI element: " + string.Join(", ", parts);
    }
}
