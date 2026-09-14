using Peek.Worker.Contracts.Automation;

namespace Peek.Core.Services.Llm;

/// <summary>
/// The first real, live consumer of Screenshot + OCR + LLM together (§13's
/// deterministic-first chain): given a focused element, decides whether OCR adds
/// anything (via IOcrDecisionService, always RunOnDemand here since the user asked
/// explicitly), assembles context, asks the LLM for a short explanation, and speaks
/// the result.
/// </summary>
public interface IElementDescriptionService
{
    Task DescribeAsync(SemanticElement element, CancellationToken ct = default);
}
