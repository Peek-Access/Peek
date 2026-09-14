namespace Peek.Core.Services.Speech;

/// <summary>
/// Who asked for an announcement - which is what decides whether it may speak, wait, or be
/// dropped when something else is already speaking.
/// </summary>
/// <remarks>
/// Peek has two fundamentally different sources of speech and they were competing on equal
/// terms, which is wrong in one specific direction: the user asks a question (analyze this
/// window, describe this element, what did I just select) and then gets talked over by the
/// hover announcement of whatever their cursor drifted across while they waited. The thing
/// they asked for loses to the thing they didn't.
/// <para>
/// The rule is deliberately asymmetric rather than a queue. Ambient speech that arrives
/// during a user-requested announcement is <em>dropped, not deferred</em>: by the time the
/// channel frees up, "button, Save" describes where the mouse was ten seconds ago, and
/// speaking it then is worse than never speaking it. A queue would turn one interruption into
/// a backlog.
/// </para>
/// </remarks>
public enum SpeechPriority
{
    /// <summary>
    /// Peek volunteered this: hover, keyboard focus, a window opening or closing. Cheap,
    /// constant, and always superseded by the next one anyway - so it is dropped outright
    /// while the channel is reserved.
    /// </summary>
    Ambient,

    /// <summary>
    /// The answer to something the user explicitly did. Interrupts ambient speech, is never
    /// dropped, and reserves the channel for as long as it is speaking.
    /// </summary>
    UserRequested,
}
