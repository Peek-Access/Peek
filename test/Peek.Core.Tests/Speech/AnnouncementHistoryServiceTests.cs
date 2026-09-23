using Peek.Core.Services.Speech;
using Peek.Core.Settings;
using Xunit;

namespace Peek.Core.Tests.Speech;

public sealed class AnnouncementHistoryServiceTests
{
    [Fact]
    public void Appending_adds_an_entry_with_the_given_window_handle()
    {
        var history = Create();

        history.Append("Save button, in Notepad", sourceWindowHandle: (nint)0x1234);

        var entry = Assert.Single(history.Entries);
        Assert.Equal("Save button, in Notepad", entry.Text);
        Assert.Equal((nint)0x1234, entry.SourceWindowHandle);
    }

    [Fact]
    public void Appending_with_no_window_handle_defaults_to_zero()
    {
        var history = Create();

        history.Append("Settings");

        Assert.Equal(default, Assert.Single(history.Entries).SourceWindowHandle);
    }

    [Fact]
    public void Disabled_history_records_nothing()
    {
        var settings = new FakeSettingsService();
        settings.Current.AnnouncementHistory.Enabled = false;
        var history = new AnnouncementHistoryService(settings);

        history.Append("hello");

        Assert.Empty(history.Entries);
    }

    [Fact]
    public void Blank_text_is_not_recorded()
    {
        var history = Create();

        history.Append("   ");

        Assert.Empty(history.Entries);
    }

    [Fact]
    public void Transcript_joins_every_entry_with_newlines_in_order()
    {
        var history = Create();

        history.Append("first");
        history.Append("second");

        Assert.Equal("first\nsecond", history.Transcript);
    }

    [Fact]
    public void Clear_empties_the_entries_and_transcript()
    {
        var history = Create();
        history.Append("hello");

        history.Clear();

        Assert.Empty(history.Entries);
        Assert.Equal("", history.Transcript);
    }

    [Fact]
    public void Oldest_entries_drop_once_the_character_cap_is_exceeded()
    {
        var settings = new FakeSettingsService();
        settings.Current.AnnouncementHistory.MaxCharacters = 1000;
        var history = new AnnouncementHistoryService(settings);

        history.Append(new string('a', 600));
        history.Append(new string('b', 600));

        // The first entry no longer fits once the second is added - dropped whole, not
        // truncated mid-line, unlike the old flat-buffer implementation.
        var entry = Assert.Single(history.Entries);
        Assert.Equal(new string('b', 600), entry.Text);
    }

    [Fact]
    public void The_most_recent_entry_survives_even_if_it_alone_exceeds_the_cap()
    {
        var settings = new FakeSettingsService();
        settings.Current.AnnouncementHistory.MaxCharacters = 1000;
        var history = new AnnouncementHistoryService(settings);

        history.Append(new string('a', 5000));

        Assert.Single(history.Entries);
    }

    private static AnnouncementHistoryService Create() => new(new FakeSettingsService());

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
