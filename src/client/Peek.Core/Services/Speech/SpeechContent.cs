namespace Peek.Core.Services.Speech;

/// <summary>
/// Structural representation of "what to say" about a UI element, built once from a
/// <c>SemanticElement</c> by an <see cref="ISpeechPolicy"/> and only then turned into
/// a spoken string - no more ad-hoc string concatenation scattered through the app (§11).
/// </summary>
public sealed record SpeechContent
{
    public required string Text { get; init; }
    public string? Role { get; init; }
    public string? State { get; init; }
    public string? Value { get; init; }
    public string? Shortcut { get; init; }
    public string? Description { get; init; }
    public string? Hint { get; init; }
}
