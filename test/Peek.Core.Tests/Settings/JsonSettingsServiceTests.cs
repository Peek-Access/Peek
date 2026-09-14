using Microsoft.Extensions.Logging.Abstractions;
using Peek.Core.Services.Ocr;
using Peek.Core.Services.Speech;
using Peek.Core.Settings;
using Xunit;

namespace Peek.Core.Tests.Settings;

public sealed class JsonSettingsServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"peek_settings_test_{Guid.NewGuid():N}.json");

    [Fact]
    public async Task First_load_creates_the_file_with_defaults()
    {
        var service = new JsonSettingsService(_path, NullLogger<JsonSettingsService>.Instance);

        await service.LoadAsync();

        Assert.True(File.Exists(_path));
        Assert.Equal(PeekSettings.CurrentVersion, service.Current.Version);
        Assert.Equal(SpeechVerbosity.Standard, service.Current.Speech.Verbosity);
        Assert.Equal(OcrUserPreference.Automatic, service.Current.Ocr.Preference);
    }

    [Fact]
    public void Synchronous_Load_also_creates_the_file_with_defaults()
    {
        var service = new JsonSettingsService(_path, NullLogger<JsonSettingsService>.Instance);

        service.Load();

        Assert.True(File.Exists(_path));
        Assert.Equal(PeekSettings.CurrentVersion, service.Current.Version);
    }

    [Fact]
    public async Task Updates_persist_and_round_trip_through_a_fresh_instance()
    {
        var first = new JsonSettingsService(_path, NullLogger<JsonSettingsService>.Instance);
        await first.LoadAsync();

        await first.UpdateAsync(s =>
        {
            s.Speech.Verbosity = SpeechVerbosity.Detailed;
            s.Ocr.Preference = OcrUserPreference.ManualOnly;
            s.Localization.UiLanguage = "zh-CN";
            s.Keyboard.Shortcuts["DescribeFocusedElement"] = "Ctrl+Shift+D";
            s.WindowAnnouncement.Enabled = true;
            s.WindowAnnouncement.AnnounceWindowOpened = false;
            s.WindowAnnouncement.Subject = WindowAnnouncementSubject.ProcessName;
        });

        var second = new JsonSettingsService(_path, NullLogger<JsonSettingsService>.Instance);
        await second.LoadAsync();

        Assert.Equal(SpeechVerbosity.Detailed, second.Current.Speech.Verbosity);
        Assert.Equal(OcrUserPreference.ManualOnly, second.Current.Ocr.Preference);
        Assert.Equal("zh-CN", second.Current.Localization.UiLanguage);
        Assert.Equal("Ctrl+Shift+D", second.Current.Keyboard.Shortcuts["DescribeFocusedElement"]);
        Assert.True(second.Current.WindowAnnouncement.Enabled);
        Assert.False(second.Current.WindowAnnouncement.AnnounceWindowOpened);
        Assert.Equal(WindowAnnouncementSubject.ProcessName, second.Current.WindowAnnouncement.Subject);
    }

    [Fact]
    public async Task Window_announcements_default_to_disabled_with_all_three_event_toggles_on()
    {
        var service = new JsonSettingsService(_path, NullLogger<JsonSettingsService>.Instance);

        await service.LoadAsync();

        var wa = service.Current.WindowAnnouncement;
        Assert.False(wa.Enabled);
        Assert.True(wa.AnnounceWindowOpened);
        Assert.True(wa.AnnounceWindowClosed);
        Assert.True(wa.AnnounceFocusChanged);
        Assert.Equal(WindowAnnouncementSubject.WindowTitle, wa.Subject);
    }

    [Fact]
    public async Task Changes_observable_emits_the_current_value_on_subscribe_then_again_per_update()
    {
        var service = new JsonSettingsService(_path, NullLogger<JsonSettingsService>.Instance);
        await service.LoadAsync();

        var emissions = 0;
        using var subscription = service.Changes.Subscribe(Observer.Create<PeekSettings>(_ => emissions++));

        await service.UpdateAsync(s => s.Accessibility.AnnounceOnHover = false);

        Assert.Equal(2, emissions); // initial (behavior-subject semantics) + the one update
    }

    [Fact]
    public async Task A_corrupt_settings_file_falls_back_to_defaults_instead_of_throwing()
    {
        await File.WriteAllTextAsync(_path, "{ not valid json");

        var service = new JsonSettingsService(_path, NullLogger<JsonSettingsService>.Instance);
        await service.LoadAsync();

        Assert.Equal(PeekSettings.CurrentVersion, service.Current.Version);
    }

    [Fact]
    public void Validate_clamps_an_invalid_speech_rate_back_to_the_default()
    {
        var settings = new PeekSettings { Speech = { Rate = -5f } };

        settings.Validate();

        Assert.Equal(1.0f, settings.Speech.Rate);
    }

    [Fact]
    public void Validate_clamps_a_non_positive_max_tokens_back_to_the_default()
    {
        var settings = new PeekSettings { Ai = { MaxTokens = 0 } };

        settings.Validate();

        Assert.Equal(120, settings.Ai.MaxTokens);
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch { /* best effort cleanup */ }
    }
}

file static class Observer
{
    public static IObserver<T> Create<T>(Action<T> onNext) => new AnonymousObserver<T>(onNext);

    private sealed class AnonymousObserver<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnNext(T value) => onNext(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }
}
