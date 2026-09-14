using Peek.Worker.Contracts.Automation;

namespace Peek.Core.Services.Llm;

/// <summary>
/// Everything <see cref="IContextAggregator"/> may combine into one request (§13).
/// Every field but <see cref="UserCommand"/> is optional - the aggregator only
/// includes what it is actually given, it never fabricates missing context.
/// </summary>
public sealed record LlmContext
{
    /// <summary>UI Automation data for the element in question, when available - always preferred over the LLM's own guess.</summary>
    public SemanticElement? FocusedElement { get; init; }

    /// <summary>Text recognized via OCR, when available.</summary>
    public string? OcrText { get; init; }

    /// <summary>What the user actually asked for (e.g. "what does this button do?", "describe this window").</summary>
    public required string UserCommand { get; init; }

    /// <summary>User-configured system prompt additions (Phase 6 Settings) - appended, never replacing the base accessibility rules.</summary>
    public string? UserSystemPromptCustomization { get; init; }
}
