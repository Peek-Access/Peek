using Microsoft.Extensions.Logging.Abstractions;
using Peek.Worker.Contracts.Tts;
using Peek.Worker.Tts;
using Xunit;

namespace Peek.Core.Tests.Services;

/// <summary>
/// The fallback exists for exactly one scenario that is otherwise catastrophic and invisible:
/// a first run with no network, where Piper can't fetch its runtime/voice and the app's only
/// output channel is the thing that's broken. These tests pin that behaviour down.
/// </summary>
public sealed class FallbackTtsServiceTests
{
    [Fact]
    public async Task Uses_the_neural_engine_when_it_works()
    {
        var primary = new StubTtsService("neural");
        var fallback = new StubTtsService("windows");
        var service = Create(primary, fallback);

        var result = await service.SpeakAsync(Request());

        Assert.Equal("neural"u8.ToArray(), result.AudioData);
        Assert.Equal(0, fallback.SpeakCalls);
    }

    [Fact]
    public async Task Falls_back_to_windows_voices_when_the_neural_engine_throws()
    {
        var primary = new StubTtsService("neural") { ThrowOnSpeak = true };
        var fallback = new StubTtsService("windows");
        var service = Create(primary, fallback);

        var result = await service.SpeakAsync(Request());

        // The user hears something rather than silence - that's the whole point.
        Assert.Equal("windows"u8.ToArray(), result.AudioData);
    }

    [Fact]
    public async Task Stays_on_the_fallback_instead_of_retrying_the_failure_every_utterance()
    {
        // Re-attempting a failing engine per utterance would add its full failure latency to
        // everything the user hears, which for a download timeout is seconds per announcement.
        var primary = new StubTtsService("neural") { ThrowOnSpeak = true };
        var fallback = new StubTtsService("windows");
        var service = Create(primary, fallback);

        await service.SpeakAsync(Request());
        await service.SpeakAsync(Request());
        await service.SpeakAsync(Request());

        Assert.Equal(1, primary.SpeakCalls);
        Assert.Equal(3, fallback.SpeakCalls);
        Assert.True(service.IsUsingFallback);
    }

    [Fact]
    public async Task Retries_the_neural_engine_once_the_cooldown_has_passed()
    {
        // A voice model that finishes downloading after the first failure has to start being
        // used without requiring a restart.
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var primary = new StubTtsService("neural") { ThrowOnSpeak = true };
        var fallback = new StubTtsService("windows");
        var service = Create(primary, fallback, time);

        await service.SpeakAsync(Request());
        primary.ThrowOnSpeak = false;

        time.Advance(FallbackTtsService.RetryPrimaryAfter + TimeSpan.FromSeconds(1));
        var result = await service.SpeakAsync(Request());

        Assert.Equal("neural"u8.ToArray(), result.AudioData);
        Assert.False(service.IsUsingFallback);
    }

    [Fact]
    public async Task A_cancelled_utterance_is_not_treated_as_an_engine_failure()
    {
        // Announcements are superseded constantly during fast navigation; counting that as
        // "the neural engine is broken" would demote everyone to Windows voices within
        // seconds of normal use.
        var primary = new StubTtsService("neural") { ThrowOnSpeak = true, ThrowCancellation = true };
        var fallback = new StubTtsService("windows");
        var service = Create(primary, fallback);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SpeakAsync(Request(), cts.Token));

        Assert.False(service.IsUsingFallback);
        Assert.Equal(0, fallback.SpeakCalls);
    }

    [Fact]
    public async Task An_internally_cancelled_utterance_is_not_treated_as_an_engine_failure()
    {
        // PiperTtsService also cancels via a token entirely of its own - a newer request's
        // Interrupt flag cancelling whatever it was still synthesizing - which has nothing to
        // do with the caller's own `ct` (still live here, unlike the test above). Treating that
        // as "the engine is broken" is exactly the bug this pins down: it used to demote every
        // user to a Windows voice - in whatever language happens to be the OS default, not the
        // user's chosen speech language - for two minutes after any ordinary fast-hover
        // interruption, and it replayed the stale, already-superseded text on top of that.
        var primary = new StubTtsService("neural") { ThrowCancellation = true };
        var fallback = new StubTtsService("windows");
        var service = Create(primary, fallback);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SpeakAsync(Request()));

        Assert.False(service.IsUsingFallback);
        Assert.Equal(0, fallback.SpeakCalls);
    }

    [Fact]
    public async Task Voice_listings_include_both_engines()
    {
        var service = Create(new StubTtsService("neural"), new StubTtsService("windows"));

        var voices = await service.GetVoicesAsync();

        Assert.Equal(2, voices.Count);
    }

    private static FallbackTtsService Create(ITtsService primary, ITtsService fallback, TimeProvider? time = null) =>
        new(primary, fallback, NullLogger<FallbackTtsService>.Instance, time);

    private static TtsSpeakRequest Request() => new() { Text = "hello" };

    private sealed class StubTtsService(string marker) : ITtsService
    {
        public bool ThrowOnSpeak { get; set; }
        public bool ThrowCancellation { get; set; }
        public int SpeakCalls { get; private set; }

        public Task<TtsSpeakResult> SpeakAsync(TtsSpeakRequest request, CancellationToken ct = default)
        {
            SpeakCalls++;
            if (ThrowCancellation) throw new OperationCanceledException(ct);
            if (ThrowOnSpeak) throw new InvalidOperationException("engine unavailable");

            return Task.FromResult(new TtsSpeakResult
            {
                AudioData = System.Text.Encoding.UTF8.GetBytes(marker),
                Format = "wav",
            });
        }

        public Task<IReadOnlyList<TtsVoiceInfo>> GetVoicesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TtsVoiceInfo>>(
                [new TtsVoiceInfo { Id = marker, Language = "en", DisplayName = marker }]);

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<TtsStatus> GetStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(new TtsStatus { IsReady = true, ActiveVoiceId = marker });
    }

    private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
