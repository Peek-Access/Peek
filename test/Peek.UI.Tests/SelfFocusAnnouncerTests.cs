using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Peek.Core.Settings;
using Peek.Ipc.Connection;
using Peek.Ipc.Worker;
using Peek.UI.Services;
using Peek.Worker.Contracts.Automation;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;
using Xunit;

namespace Peek.UI.Tests;

public class SelfFocusAnnouncerTests
{
    [AvaloniaFact]
    public async Task Focusing_A_Control_Announces_It()
    {
        var speech = new RecordingSpeechService();
        var settings = new FakeSettingsService();
        var connection = CreateReadyWorkerConnection();
        using var announcer = new SelfFocusAnnouncer(connection, speech, settings, NullLogger<SelfFocusAnnouncer>.Instance);

        var button = new Button();
        AutomationProperties.SetName(button, "Test Button");
        var window = new Window { Content = button };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        button.Focus();
        Dispatcher.UIThread.RunJobs();

        await WaitForAsync(() => speech.AnnouncedText.Count > 0);

        Assert.Contains("Test Button", speech.AnnouncedText);

        window.Close();
        await connection.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task AnnounceOwnInterface_Off_Suppresses_Announcements()
    {
        var speech = new RecordingSpeechService();
        var settings = new FakeSettingsService();
        settings.Current.Accessibility.AnnounceOwnInterface = false;
        var connection = CreateReadyWorkerConnection();
        using var announcer = new SelfFocusAnnouncer(connection, speech, settings, NullLogger<SelfFocusAnnouncer>.Instance);

        var button = new Button();
        AutomationProperties.SetName(button, "Test Button");
        var window = new Window { Content = button };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        button.Focus();
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(200);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(speech.AnnouncedText);

        window.Close();
        await connection.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Disposed_Announcer_Stops_Announcing()
    {
        var speech = new RecordingSpeechService();
        var settings = new FakeSettingsService();
        var connection = CreateReadyWorkerConnection();
        var announcer = new SelfFocusAnnouncer(connection, speech, settings, NullLogger<SelfFocusAnnouncer>.Instance);
        announcer.Dispose();

        var button = new Button();
        AutomationProperties.SetName(button, "Test Button");
        var window = new Window { Content = button };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        button.Focus();
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(200);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(speech.AnnouncedText);

        window.Close();
        await connection.DisposeAsync();
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// WorkerConnection is a sealed class with no interface and no public way to reach
    /// ConnectionState.Ready short of a real worker process - SelfFocusAnnouncer.AnnounceAsync
    /// bails out unless CurrentState == Ready, so a test exercising the announce path has to
    /// reach into the private state subject directly. If this starts throwing, WorkerConnection
    /// was refactored and this needs to move to whatever seam replaced it.
    /// </summary>
    private static WorkerConnection CreateReadyWorkerConnection()
    {
        var connection = new WorkerConnection(new WorkerConnectionOptions(), NullLoggerFactory.Instance);

        var field = typeof(WorkerConnection).GetField("_stateSubject", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("WorkerConnection._stateSubject not found - it may have been renamed.");
        var stateSubject = field.GetValue(connection)
            ?? throw new InvalidOperationException("WorkerConnection._stateSubject was null.");
        var onNext = stateSubject.GetType().GetMethod("OnNext")
            ?? throw new InvalidOperationException("BehaviorSignal<ConnectionState>.OnNext not found.");
        onNext.Invoke(stateSubject, [ConnectionState.Ready]);

        return connection;
    }
}

/// <summary>Records announcements instead of doing real TTS.</summary>
internal sealed class RecordingSpeechService : Peek.Core.Services.Speech.IAccessibilitySpeechService
{
    public List<SemanticElement> AnnouncedElements { get; } = [];
    public List<string> AnnouncedText { get; } = [];

    public Task AnnounceAsync(SemanticElement element, Peek.Core.Services.Speech.SpeechPriority priority, CancellationToken ct = default)
    {
        AnnouncedElements.Add(element);
        if (!string.IsNullOrEmpty(element.Name))
            AnnouncedText.Add(element.Name);
        return Task.CompletedTask;
    }

    public Task AnnounceTextAsync(
        string text,
        Peek.Core.Services.Speech.SpeechPriority priority,
        nint sourceWindowHandle = default,
        bool recordInHistory = true,
        CancellationToken ct = default)
    {
        AnnouncedText.Add(text);
        return Task.CompletedTask;
    }

    public IDisposable BeginExclusive(string reason, Action? onStopRequested = null) => new NoopDisposable();

    public bool IsChannelReserved => false;

    public bool IsSpeaking => false;

    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

/// <summary>In-memory settings - resolves AnnounceOwnInterface/FocusThrottleMs without touching the real settings.json.</summary>
internal sealed class FakeSettingsService : ISettingsService
{
    public PeekSettings Current { get; } = new() { Accessibility = { FocusThrottleMs = 1 } };

    public IObservable<PeekSettings> Changes => new Signal<PeekSettings>().AsObservable();

    public void Load() { }

    public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task UpdateAsync(Action<PeekSettings> mutate, CancellationToken ct = default)
    {
        mutate(Current);
        return Task.CompletedTask;
    }
}
