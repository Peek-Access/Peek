using System.Drawing;
using Microsoft.Extensions.Logging;
using Peek.Worker.Contracts.Automation;
using Peek.Core.Abstractions;
using Peek.Core.Services.Speech;
using Peek.Core.Settings;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace Peek.Integration.Tests;

/// <summary>Diagnostic-only ILogger&lt;T&gt; that writes straight to Console.Out - avoids pulling in a
/// Microsoft.Extensions.Logging.Console package reference just to see debug output while
/// troubleshooting a test locally.</summary>
internal sealed class ConsoleLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Console.WriteLine($"[{logLevel}] {typeof(T).Name}: {formatter(state, exception)}{(exception is null ? "" : "\n" + exception)}");
}

/// <summary>Controllable IMouseTracker - the test pushes positions instead of a real hardware hook.</summary>
internal sealed class FakeMouseTracker : IMouseTracker
{
    private readonly Signal<Point> _position = new();
    private readonly Signal<RxVoid> _selected = new();

    public IObservable<Point> MousePositionStream => _position.AsObservable();
    public IObservable<RxVoid> SelectedStream => _selected.AsObservable();

    public bool IsPaused { get; private set; }

    public void Push(Point point) => _position.OnNext(point);

    public void Pause() => IsPaused = true;
    public void Resume() => IsPaused = false;
}

/// <summary>No-op IHighlightService - these tests run with no Avalonia UI thread/window to draw an overlay onto. Records every UpdateLocation call so a test can assert on what the box would have been drawn at (e.g. following an individual OCR line rather than the whole window).</summary>
internal sealed class FakeHighlightService : IHighlightService
{
    public List<Rectangle> UpdatedLocations { get; } = [];

    public void Initialize() { }
    public void Show(Rectangle rect, nint? targetHwnd = null) { }
    public void Hide() { }
    public void Resume() { }
    public void Reset() { }
    public void UpdateLocation(Rectangle rect) => UpdatedLocations.Add(rect);
    public Task UpdateLocationAsync(Rectangle rect) => Task.CompletedTask;
    public void Clear() { }
    public void UpdateColorsRandomly() { }
    public Task StartBreathAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopBreathAsync() => Task.CompletedTask;
}

/// <summary>Records announcements instead of doing real TTS - keeps this regression test fast and independent of Piper/audio.</summary>
internal sealed class RecordingSpeechService : IAccessibilitySpeechService
{
    public List<SemanticElement> AnnouncedElements { get; } = [];
    public List<string> AnnouncedText { get; } = [];

    /// <summary>Priority of each announcement, in order - so a test can assert on which kind it was.</summary>
    public List<SpeechPriority> AnnouncedPriorities { get; } = [];

    public int StopCount { get; private set; }
    public int ExclusiveLeases { get; private set; }

    public Task StopAsync(CancellationToken ct = default)
    {
        StopCount++;
        return Task.CompletedTask;
    }

    public Task AnnounceAsync(SemanticElement element, SpeechPriority priority, CancellationToken ct = default)
    {
        AnnouncedPriorities.Add(priority);
        AnnouncedElements.Add(element);
        return Task.CompletedTask;
    }

    public Task AnnounceTextAsync(string text, SpeechPriority priority, CancellationToken ct = default)
    {
        AnnouncedPriorities.Add(priority);
        AnnouncedText.Add(text);
        return Task.CompletedTask;
    }

    public IDisposable BeginExclusive(string reason, Action? onStopRequested = null)
    {
        ExclusiveLeases++;
        return new ActionDisposable(() => ExclusiveLeases--);
    }

    public bool IsChannelReserved => ExclusiveLeases > 0;

    public bool IsSpeaking => throw new NotImplementedException();

    private sealed class ActionDisposable(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}

/// <summary>
/// In-memory settings, so ElementTracker's hover throttles and AnnounceOnHover gate resolve
/// to their defaults without touching the real settings.json on the developer's machine.
/// </summary>
internal sealed class FakeSettingsService : ISettingsService
{
    private readonly Signal<PeekSettings> _changes = new();

    public PeekSettings Current { get; } = new();

    public IObservable<PeekSettings> Changes => _changes;

    public void Load() { }

    public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task UpdateAsync(Action<PeekSettings> mutate, CancellationToken ct = default)
    {
        mutate(Current);
        _changes.OnNext(Current);
        return Task.CompletedTask;
    }
}
