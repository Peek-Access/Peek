using Peek.Worker.Contracts.Llm;

namespace Peek.Core.Services.Llm;

/// <summary>
/// Turns an <see cref="LlmContext"/> into the message list <c>ILlmProvider.CompleteAsync</c>
/// expects, applying the deterministic-first ordering from §13: UIA/OCR facts are
/// handed to the model as given context, never re-derived or invented by it.
/// </summary>
public interface IContextAggregator
{
    IReadOnlyList<LlmMessage> BuildMessages(LlmContext context);
}
