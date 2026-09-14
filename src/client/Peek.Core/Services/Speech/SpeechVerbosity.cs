namespace Peek.Core.Services.Speech;

/// <summary>
/// How much of a <see cref="SpeechContent"/> gets spoken. Only <see cref="Standard"/>
/// has a real formatter today (see <c>StandardSpeechPolicy</c>); the rest are the
/// extension point for the real Settings model (verbosity is user-configurable per
/// §10), not built out until that phase lands.
/// </summary>
public enum SpeechVerbosity
{
    Minimal,
    Standard,
    Detailed,
    Verbose,
}
