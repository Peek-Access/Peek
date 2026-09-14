using Microsoft.Extensions.Logging.Abstractions;
using Peek.Core.Services;
using Peek.Core.Services.Speech;
using Peek.Core.Settings;
using Peek.Ipc.Worker;
using Xunit;

namespace Peek.Core.Tests.Speech;

/// <summary>
/// Arbitration of the single speech channel: who gets to talk when two things want to at once.
/// </summary>
/// <remarks>
/// The rule exists because the losing side was always the wrong one. The user asks Peek to
/// analyse a window - a request that takes seconds and answers in several sentences - and
/// while they wait, their cursor drifts over a button and the hover announcement cuts the
/// answer off. Ambient speech is Peek talking to itself; user-requested speech is Peek
/// answering a question. The second must win, and must keep winning across the gaps between
/// its own sentences, which is what <see cref="IAccessibilitySpeechService.BeginExclusive"/>
/// covers.
/// <para>
/// Asserted through the announcement history rather than through audio: a dropped announcement
/// is never spoken, so it is never recorded either, which makes the transcript an honest
/// record of what the user actually heard.
/// </para>
/// </remarks>
public sealed class SpeechPriorityTests
{
    [Fact]
    public void Nothing_is_reserved_to_begin_with()
    {
        using var speech = Create(out _);

        Assert.False(speech.IsChannelReserved);
    }

    [Fact]
    public void An_exclusive_lease_reserves_the_channel_until_disposed()
    {
        using var speech = Create(out _);

        var lease = speech.BeginExclusive("AI analysis");
        Assert.True(speech.IsChannelReserved);

        lease.Dispose();
        Assert.False(speech.IsChannelReserved);
    }

    [Fact]
    public void Overlapping_leases_all_have_to_be_released()
    {
        // Two long operations can overlap; the channel is free only when the last one is done.
        using var speech = Create(out _);

        var first = speech.BeginExclusive("analysis");
        var second = speech.BeginExclusive("description");

        first.Dispose();
        Assert.True(speech.IsChannelReserved);

        second.Dispose();
        Assert.False(speech.IsChannelReserved);
    }

    [Fact]
    public void Disposing_a_lease_twice_does_not_free_someone_elses()
    {
        using var speech = Create(out _);

        var first = speech.BeginExclusive("analysis");
        var second = speech.BeginExclusive("description");

        first.Dispose();
        first.Dispose();

        Assert.True(speech.IsChannelReserved);
        second.Dispose();
        Assert.False(speech.IsChannelReserved);
    }

    [Fact]
    public async Task Ambient_announcements_are_dropped_while_the_channel_is_reserved()
    {
        using var speech = Create(out var history);
        using var lease = speech.BeginExclusive("AI analysis");

        await speech.AnnounceTextAsync("the mouse moved over a button", SpeechPriority.Ambient);

        // Never spoken, so never recorded - and specifically not queued for later, because by
        // then it would describe where the cursor was, not where it is.
        Assert.DoesNotContain("the mouse moved over a button", history.Transcript);
    }

    [Fact]
    public async Task Ambient_announcements_go_through_once_the_channel_is_free()
    {
        using var speech = Create(out var history);

        speech.BeginExclusive("AI analysis").Dispose();
        await speech.AnnounceTextAsync("hello", SpeechPriority.Ambient);

        Assert.Contains("hello", history.Transcript);
    }

    [Fact]
    public async Task A_user_requested_announcement_is_never_dropped()
    {
        // The whole point of the distinction: an explicit request is not ambient chatter and
        // does not lose to a reservation.
        using var speech = Create(out var history);
        using var lease = speech.BeginExclusive("AI analysis");

        await speech.AnnounceTextAsync("Save button, in Notepad", SpeechPriority.UserRequested);

        Assert.Contains("Save button, in Notepad", history.Transcript);
    }

    [Fact]
    public async Task Asking_for_silence_tells_the_channel_owner_to_give_up()
    {
        // "Be quiet" has to outrank even the operation holding the channel. Without this it
        // silenced the sentence being spoken and then the next one started, and the user had
        // to keep pressing the key to win an argument with the app.
        using var speech = Create(out _);
        var cancelled = false;

        using var lease = speech.BeginExclusive("AI analysis", onStopRequested: () => cancelled = true);

        await speech.StopAsync();

        Assert.True(cancelled);
    }

    [Fact]
    public async Task A_failing_stop_callback_does_not_prevent_silence()
    {
        using var speech = Create(out _);
        using var lease = speech.BeginExclusive("AI analysis", onStopRequested: () => throw new InvalidOperationException("boom"));

        // The user gets silence regardless of whether the owner handled the request cleanly.
        await speech.StopAsync();
    }

    private static AccessibilitySpeechService Create(out AnnouncementHistoryService history)
    {
        var settings = new FakeSettingsService();
        history = new AnnouncementHistoryService(settings);

        // A never-started connection: every test here asserts on a decision made before any
        // worker call, and the one path that would reach the worker (StopAsync) already treats
        // an unreachable worker as a non-event.
        var connection = new WorkerConnection(
            new WorkerConnectionOptions { ManageWorkerProcess = false, PipeName = $"peek-test-{Guid.NewGuid():N}" },
            NullLoggerFactory.Instance);

        return new AccessibilitySpeechService(
            connection,
            new AudioPlayer(NullLogger<AudioPlayer>.Instance),
            new StandardSpeechPolicy(),
            settings,
            history,
            NullLogger<AccessibilitySpeechService>.Instance);
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public PeekSettings Current { get; } = new();

        public IObservable<PeekSettings> Changes => throw new NotSupportedException();

        public void Load() { }

        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateAsync(Action<PeekSettings> update, CancellationToken ct = default)
        {
            update(Current);
            return Task.CompletedTask;
        }
    }
}
