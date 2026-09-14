using Xunit;

namespace Peek.Integration.Tests;

/// <summary>
/// Speech is the product. These drive <c>tts.speak</c> over the real pipe to a real worker
/// process and assert that audio comes back.
/// </summary>
/// <remarks>
/// Run against the *published* worker (<c>Peek_TEST_WORKER_PATH</c>), these are also the trim
/// check for both engines: a plain Debug build cannot fail the way a trimmed publish can,
/// since trimming can unroot a type a speech engine depends on (System.Speech) and turn the
/// first Speak call into an access violation that kills the worker process outright.
/// <para>
/// Set <c>PEEK_TTS_ENGINE=windows</c> to point the worker at the SAPI fallback; otherwise it
/// runs the normal Piper-with-fallback stack. Both configurations matter and neither is
/// reachable from the other, since the fallback only engages once Piper has already failed.
/// </para>
/// </remarks>
public sealed class TtsRoundTripTests(TestWorkerFixture fixture) : IClassFixture<TestWorkerFixture>
{
    [Fact]
    public async Task Speaking_returns_playable_audio()
    {
        var result = await fixture.Connection.Client.Tts.SpeakAsync(
            "Peek is ready.", voiceId: null, rate: 1.0f, interrupt: true);

        Assert.NotNull(result.AudioData);
        Assert.NotEmpty(result.AudioData);

        // A WAV file, not an empty buffer or an error payload that happens to be non-empty.
        Assert.Equal("RIFF"u8.ToArray(), result.AudioData[..4]);
    }

    [Fact]
    public async Task The_worker_survives_repeated_synthesis()
    {
        // The trimming failure was a hard process crash rather than an exception, so the
        // meaningful assertion is that the worker is still answering afterwards.
        for (var i = 0; i < 3; i++)
        {
            var result = await fixture.Connection.Client.Tts.SpeakAsync(
                $"Announcement number {i}.", voiceId: null, rate: 1.0f, interrupt: true);

            Assert.NotEmpty(result.AudioData);
        }

        Assert.True(await fixture.Connection.Client.Automation.PingAsync());
    }

    [Fact]
    public async Task Voices_can_be_listed()
    {
        var voices = await fixture.Connection.Client.Tts.GetVoicesAsync();

        Assert.NotEmpty(voices);
    }
}
