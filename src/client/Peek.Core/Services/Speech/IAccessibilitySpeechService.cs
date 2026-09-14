using Peek.Worker.Contracts.Automation;

namespace Peek.Core.Services.Speech;

/// <summary>
/// Announces a UIA element through the structured speech pipeline: SemanticElement -&gt;
/// ISpeechPolicy -&gt; SpeechContent -&gt; Peek.Worker's TTS -&gt; local playback (§10/§11).
/// </summary>
/// <remarks>
/// Every call states a <see cref="SpeechPriority"/>, and that is what arbitrates the single
/// speech channel: a <see cref="SpeechPriority.UserRequested"/> announcement interrupts
/// ambient chatter and reserves the channel until it has finished, while ambient announcements
/// that arrive during it are dropped. The parameter is required rather than defaulted so that
/// adding a new source of speech forces a decision about which of the two it is - defaulting
/// it would silently make every future caller ambient.
/// </remarks>
public interface IAccessibilitySpeechService
{
    Task AnnounceAsync(SemanticElement element, SpeechPriority priority, CancellationToken ct = default);

    /// <summary>
    /// Speaks arbitrary text that didn't come from an ISpeechPolicy formatting pass
    /// (an OCR suggestion message, an LLM-generated description, a settings preview
    /// phrase, ...) through the same voice/rate/priority pipeline as <see cref="AnnounceAsync"/>.
    /// </summary>
    Task AnnounceTextAsync(string text, SpeechPriority priority, CancellationToken ct = default);

    /// <summary>
    /// Reserves the speech channel for an operation that speaks over several utterances and a
    /// long stretch of time - an AI analysis narrates sentence by sentence as the model
    /// produces them, with pauses in between. Per-utterance priority alone cannot cover those
    /// gaps: a hover announcement landing between two sentences would cut the narration in
    /// half. Ambient speech is dropped for as long as the returned lease is held.
    /// </summary>
    /// <param name="reason">Short description of the owning operation, for the log.</param>
    /// <param name="onStopRequested">
    /// Invoked if the user asks for silence (<see cref="StopAsync"/>) while the lease is held,
    /// so the owner can abandon the rest of its work. Without this, "be quiet" would silence
    /// the current sentence and then be talked over by the next one - the user having to press
    /// it repeatedly to win an argument with the app.
    /// </param>
    IDisposable BeginExclusive(string reason, Action? onStopRequested = null);

    /// <summary>True while a user-requested announcement or an exclusive lease owns the channel.</summary>
    bool IsChannelReserved { get; }

    /// <summary>True while any synthesized audio is being played.</summary>
    bool IsSpeaking { get; }

    /// <summary>
    /// Stops whatever is currently being synthesized or played, immediately, without
    /// speaking anything in its place - the "be quiet" command every screen reader needs a
    /// dedicated key for (NVDA and JAWS both put it on Ctrl). Without it the only way to cut
    /// off a long announcement - an LLM description can run several hundred characters - is
    /// to trigger a different announcement over the top of it, or wait it out.
    /// Also cancels any exclusive lease holder, since silence requested by the user outranks
    /// everything, including the operation that reserved the channel.
    /// Safe to call when nothing is speaking.
    /// </summary>
    Task StopAsync(CancellationToken ct = default);
}
