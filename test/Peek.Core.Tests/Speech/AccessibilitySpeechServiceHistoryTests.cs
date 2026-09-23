using Microsoft.Extensions.Logging.Abstractions;
using Peek.Core.Services;
using Peek.Core.Services.Speech;
using Peek.Core.Settings;
using Peek.Ipc.Worker;
using Xunit;

namespace Peek.Core.Tests.Speech;

/// <summary>
/// AccessibilitySpeechService's own responsibility for the two knobs Announcement History's
/// replay feature relies on: recording the source window a spoken text was about, and being
/// able to opt a specific announcement (a replay of an existing entry) out of history entirely.
/// </summary>
public sealed class AccessibilitySpeechServiceHistoryTests
{
    [Fact]
    public async Task AnnounceTextAsync_records_the_given_source_window_handle()
    {
        using var speech = Create(out var history);

        await speech.AnnounceTextAsync("Notepad is ready", SpeechPriority.UserRequested, sourceWindowHandle: (nint)0x42);

        Assert.Equal((nint)0x42, Assert.Single(history.Entries).SourceWindowHandle);
    }

    [Fact]
    public async Task AnnounceTextAsync_defaults_to_no_window_handle()
    {
        using var speech = Create(out var history);

        await speech.AnnounceTextAsync("Settings", SpeechPriority.UserRequested);

        Assert.Equal(default, Assert.Single(history.Entries).SourceWindowHandle);
    }

    [Fact]
    public async Task RecordInHistory_false_speaks_without_adding_a_new_entry()
    {
        // Announcement History's own replay action: re-speaking an entry must not grow the
        // history it was replayed from.
        using var speech = Create(out var history);
        await speech.AnnounceTextAsync("original", SpeechPriority.UserRequested);

        await speech.AnnounceTextAsync("original", SpeechPriority.UserRequested, recordInHistory: false);

        Assert.Single(history.Entries);
    }

    private static AccessibilitySpeechService Create(out AnnouncementHistoryService history)
    {
        var settings = new FakeSettingsService();
        history = new AnnouncementHistoryService(settings);

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
