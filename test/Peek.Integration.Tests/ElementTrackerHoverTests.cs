using System.Diagnostics;
using System.Drawing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Peek.Core.Abstractions;
using Peek.Core.Services;
using Peek.Core.Services.Ocr;
using Peek.Core.Settings;
using Peek.Core.Services.Speech;
using Peek.Ipc.Worker;
using Peek.Ipc.Connection;
using Xunit;

namespace Peek.Integration.Tests;

/// <summary>
/// Verifies ElementTracker's first-ever "start tracking" call announces the hovered element:
/// the "already tracking, just resume" guard must be able to distinguish "never started" from
/// "started and currently active", or the very first enable silently does nothing (no
/// automation query, no announcement - hovering never speaks anything). This test drives the
/// real reactive pipeline -
/// IsTracking -> mouse position -> WorkerConnection -> automation.getElementFromPoint
/// -> CurrentElement - end to end against a real worker and a real window, so it
/// fails the same way a user hovering an element would notice: nothing happens.
/// </summary>
/// <remarks>
/// Deliberately does NOT use a shared, pre-started <see cref="TestWorkerFixture"/>
/// connection the way <see cref="AutomationRoundTripTests"/>/<see cref="OcrRoundTripTests"/>
/// do, for two reasons:
/// <list type="bullet">
/// <item><see cref="ElementTracker.Dispose"/> tears down its <see cref="WorkerConnection"/>
/// as part of normal (production) shutdown, so a tracker built around a
/// class-fixture-shared connection would kill that connection - and every other test in
/// the class, plus the fixture's own teardown - the moment the first test's
/// `using var tracker` goes out of scope.</item>
/// <item><see cref="ElementTracker"/> always calls <see cref="WorkerConnection.StartAsync"/>
/// itself (matching how the real app hands it a fresh, unstarted connection). Handing it
/// an already-<see cref="ConnectionState.Ready"/> connection makes it start a *second*
/// worker process and briefly drop back out of Ready while doing so - which silently
/// swallows the single, non-replayed mouse-position push these tests rely on, since
/// TrackMouseElement's inner query observable isn't subscribed while not Ready.</item>
/// </list>
/// Each test builds and owns its own never-started connection/worker process instead.
/// </remarks>
public sealed class ElementTrackerHoverTests
{
    [Fact]
    public async Task Enabling_tracking_for_the_first_time_announces_the_hovered_element()
    {
        var connection = CreateUnstartedConnection();

        using var window = new TestWindow("ElementTracker Hover Test", "Save Changes");

        var mouseTracker = new FakeMouseTracker();
        var speechService = new RecordingSpeechService();

        var services = new ServiceCollection();
        services.AddSingleton<IMouseTracker>(mouseTracker);
        services.AddSingleton(connection);
        services.AddSingleton<IHighlightService>(new FakeHighlightService());
        services.AddSingleton<IAccessibilitySpeechService>(speechService);
        services.AddSingleton<ISettingsService>(new FakeSettingsService());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<WindowEnumerator>();
        services.AddSingleton<IOcrDecisionService, DefaultOcrDecisionService>();
        services.AddSingleton<OcrFallbackAnnouncer>();
        var provider = services.BuildServiceProvider();

        // ElementTracker's ctor kicks off InitializeAsync() -> connection.StartAsync()
        // itself; this is the first-ever "start tracking" transition under test.
        using var tracker = new ElementTracker(provider, NullLogger<ElementTracker>.Instance);

        // First-ever enable - the transition that must actually announce something.
        tracker.IsTracking = true;
        await WaitUntilAsync(() => !mouseTracker.IsPaused, TimeSpan.FromSeconds(2),
            "mouse tracker was never resumed - IsTracking's reactive subscription didn't fire StartTrackingCore");

        await WaitUntilAsync(() => connection.CurrentState == ConnectionState.Ready, TimeSpan.FromSeconds(20),
            "worker connection never reached Ready");

        // The window is off-screen but still has a real, hit-testable HWND/rect -
        // push the button's center point in screen coordinates.
        mouseTracker.Push(window.ButtonCenterScreenPoint);

        var element = await WaitForValueAsync(() => tracker.CurrentElement, TimeSpan.FromSeconds(10),
            "CurrentElement never became non-null - hovering must produce an " +
            "automation.getElementFromPoint result");

        Assert.Equal("Save Changes", element.Name);
        Assert.Equal("Button", element.ControlType);

        await WaitUntilAsync(() => speechService.AnnouncedElements.Count > 0, TimeSpan.FromSeconds(2),
            "the element was found but never reached IAccessibilitySpeechService.AnnounceAsync");
        Assert.Equal("Save Changes", speechService.AnnouncedElements[0].Name);
    }

    [Fact]
    public async Task Disabling_then_reenabling_tracking_keeps_working()
    {
        var connection = CreateUnstartedConnection();

        using var window = new TestWindow("ElementTracker Resume Test", "Cancel");

        var mouseTracker = new FakeMouseTracker();
        var speechService = new RecordingSpeechService();

        var services = new ServiceCollection();
        services.AddSingleton<IMouseTracker>(mouseTracker);
        services.AddSingleton(connection);
        services.AddSingleton<IHighlightService>(new FakeHighlightService());
        services.AddSingleton<IAccessibilitySpeechService>(speechService);
        services.AddSingleton<ISettingsService>(new FakeSettingsService());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<WindowEnumerator>();
        services.AddSingleton<IOcrDecisionService, DefaultOcrDecisionService>();
        services.AddSingleton<OcrFallbackAnnouncer>();
        var provider = services.BuildServiceProvider();

        using var tracker = new ElementTracker(provider, NullLogger<ElementTracker>.Instance);

        tracker.IsTracking = true;
        await WaitUntilAsync(() => !mouseTracker.IsPaused, TimeSpan.FromSeconds(2), "did not resume on first enable");

        tracker.IsTracking = false;
        await WaitUntilAsync(() => mouseTracker.IsPaused, TimeSpan.FromSeconds(2), "did not pause on disable");

        tracker.IsTracking = true;
        await WaitUntilAsync(() => !mouseTracker.IsPaused, TimeSpan.FromSeconds(2), "did not resume on re-enable");

        await WaitUntilAsync(() => connection.CurrentState == ConnectionState.Ready, TimeSpan.FromSeconds(20),
            "worker connection never reached Ready");

        mouseTracker.Push(window.ButtonCenterScreenPoint);

        var element = await WaitForValueAsync(() => tracker.CurrentElement, TimeSpan.FromSeconds(10),
            "re-enabling tracking after a pause did not produce an element");
        Assert.Equal("Cancel", element.Name);
    }

    /// <summary>
    /// Reproduces the reported bug directly: a window UI Automation cannot drill into at all
    /// (see OpaqueTestWindow) - every hover resolves to the same titled-but-childless Window
    /// element, the same shape WeChat/Weixin's message list produced in the field (see
    /// docs/OCR_STRATEGY.md). The test process's own name stands in for "Weixin"/"WeChat" on
    /// KnownProblematicApplications - production ships those two seeded by default, so a real
    /// WeChat window takes exactly this path.
    /// </summary>
    [Fact]
    public async Task Hovering_a_known_problematic_UIA_opaque_window_reads_its_content_via_OCR()
    {
        var connection = CreateUnstartedConnection();

        using var window = new OpaqueTestWindow("Untestable Chat App", "HELLO FROM OCR");

        var mouseTracker = new FakeMouseTracker();
        var speechService = new RecordingSpeechService();
        var settings = new FakeSettingsService();
        settings.Current.Ocr.KnownProblematicApplications.Clear();
        settings.Current.Ocr.KnownProblematicApplications.Add(Process.GetCurrentProcess().ProcessName);

        var services = new ServiceCollection();
        services.AddSingleton<IMouseTracker>(mouseTracker);
        services.AddSingleton(connection);
        services.AddSingleton<IHighlightService>(new FakeHighlightService());
        services.AddSingleton<IAccessibilitySpeechService>(speechService);
        services.AddSingleton<ISettingsService>(settings);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<WindowEnumerator>();
        services.AddSingleton<IOcrDecisionService, DefaultOcrDecisionService>();
        services.AddSingleton<OcrFallbackAnnouncer>();
        var provider = services.BuildServiceProvider();

        using var tracker = new ElementTracker(provider, NullLogger<ElementTracker>.Instance);

        tracker.IsTracking = true;
        await WaitUntilAsync(() => !mouseTracker.IsPaused, TimeSpan.FromSeconds(2), "did not resume on enable");
        await WaitUntilAsync(() => connection.CurrentState == ConnectionState.Ready, TimeSpan.FromSeconds(20),
            "worker connection never reached Ready");

        mouseTracker.Push(window.ScreenPointForLine(0));

        // A screenshot+OCR round trip is slower than a plain hover announcement, hence the
        // longer timeout than the other tests in this class use. Waits for the recognized text
        // specifically, not just "some announcement arrived" - RunAndAnnounceAsync now also
        // speaks a preliminary "Extracting text..." cue ahead of the real OCR result, and that
        // alone would satisfy a plain Count > 0 check before the actual content ever arrives.
        await WaitUntilAsync(() => speechService.AnnouncedText.Any(t => t.Contains("HELLO", StringComparison.OrdinalIgnoreCase)),
            TimeSpan.FromSeconds(20),
            "hovering the UIA-opaque window never produced an OCR-based announcement - only the " +
            "plain UIA one (or none at all) - see AnnouncedElements/AnnouncedText for what actually happened");

        // The plain "title, window" announcement must have been suppressed in favor of the
        // OCR text, not spoken alongside or instead of it.
        Assert.Empty(speechService.AnnouncedElements);
    }

    /// <summary>
    /// The same opaque window, but NOT on KnownProblematicApplications - proving the list is an
    /// optional fast-path (skip the probe for names already known), not a requirement for
    /// automatic reading to work. An app nobody has ever hand-listed still gets read
    /// automatically the moment OcrFallbackAnnouncer's whole-window probe
    /// (IsWindowUiaOpaqueAsync) confirms it exposes nothing - that confirmation is treated as at
    /// least as trustworthy as a name match, since it's inspected rather than guessed (see
    /// docs/OCR_STRATEGY.md). Without a persisted settings.json ever being edited by hand, this
    /// is also exactly what makes the fix work out of the box against a real app UIA can't see
    /// into, not just the two names shipped by default.
    /// </summary>
    [Fact]
    public async Task Hovering_an_unlisted_UIA_opaque_window_still_reads_its_content_via_OCR()
    {
        var connection = CreateUnstartedConnection();

        using var window = new OpaqueTestWindow("Untestable Chat App", "HELLO FROM OCR");

        var mouseTracker = new FakeMouseTracker();
        var speechService = new RecordingSpeechService();
        var settings = new FakeSettingsService();
        settings.Current.Ocr.KnownProblematicApplications.Clear();

        var services = new ServiceCollection();
        services.AddSingleton<IMouseTracker>(mouseTracker);
        services.AddSingleton(connection);
        services.AddSingleton<IHighlightService>(new FakeHighlightService());
        services.AddSingleton<IAccessibilitySpeechService>(speechService);
        services.AddSingleton<ISettingsService>(settings);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<WindowEnumerator>();
        services.AddSingleton<IOcrDecisionService, DefaultOcrDecisionService>();
        services.AddSingleton<OcrFallbackAnnouncer>();
        var provider = services.BuildServiceProvider();

        using var tracker = new ElementTracker(provider, NullLogger<ElementTracker>.Instance);

        tracker.IsTracking = true;
        await WaitUntilAsync(() => !mouseTracker.IsPaused, TimeSpan.FromSeconds(2), "did not resume on enable");
        await WaitUntilAsync(() => connection.CurrentState == ConnectionState.Ready, TimeSpan.FromSeconds(20),
            "worker connection never reached Ready");

        mouseTracker.Push(window.ScreenPointForLine(0));

        // Waits for the recognized text specifically (see the sibling test above for why a
        // plain Count > 0 check races against the preliminary "Extracting text..." cue).
        await WaitUntilAsync(() => speechService.AnnouncedText.Any(t => t.Contains("HELLO", StringComparison.OrdinalIgnoreCase)),
            TimeSpan.FromSeconds(20),
            "hovering the unlisted UIA-opaque window never produced an OCR-based announcement - " +
            "either the opacity probe didn't identify it as opaque, or OCR/announce failed silently");

        Assert.Empty(speechService.AnnouncedElements);
    }

    /// <summary>
    /// The gap this class exists to close: moving to a different line resolves to the *same*
    /// UIA Window element every time (see the other OpaqueTestWindow tests), so ElementTracker's
    /// UIA-identity-based dedup never lets the hover-announcement pipeline fire a second time
    /// once inside the window - OCR text would otherwise be read once on entry and then go
    /// completely deaf to further mouse movement, unlike every UIA-backed element. Proves the
    /// separate position-follow subscription (ElementTracker's second MousePositionStream
    /// subscription, feeding OcrFallbackAnnouncer.TryAnnounceForPositionAsync) picks up where
    /// the UIA-identity pipeline stops: after the first line is read, moving to a second,
    /// distinct line gets that line specifically, not silence and not the first line again.
    /// </summary>
    [Fact]
    public async Task Moving_to_a_different_line_in_a_UIA_opaque_window_announces_that_line()
    {
        var connection = CreateUnstartedConnection();

        using var window = new OpaqueTestWindow("Untestable Chat App", "FIRST LINE HERE", "SECOND LINE HERE");

        var mouseTracker = new FakeMouseTracker();
        var speechService = new RecordingSpeechService();
        var settings = new FakeSettingsService();
        settings.Current.Ocr.KnownProblematicApplications.Add(Process.GetCurrentProcess().ProcessName);

        var services = new ServiceCollection();
        services.AddSingleton<IMouseTracker>(mouseTracker);
        services.AddSingleton(connection);
        services.AddSingleton<IHighlightService>(new FakeHighlightService());
        services.AddSingleton<IAccessibilitySpeechService>(speechService);
        services.AddSingleton<ISettingsService>(settings);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<WindowEnumerator>();
        services.AddSingleton<IOcrDecisionService, DefaultOcrDecisionService>();
        services.AddSingleton<OcrFallbackAnnouncer>();
        var provider = services.BuildServiceProvider();

        using var tracker = new ElementTracker(provider, NullLogger<ElementTracker>.Instance);

        tracker.IsTracking = true;
        await WaitUntilAsync(() => !mouseTracker.IsPaused, TimeSpan.FromSeconds(2), "did not resume on enable");
        await WaitUntilAsync(() => connection.CurrentState == ConnectionState.Ready, TimeSpan.FromSeconds(20),
            "worker connection never reached Ready");

        // First hover: goes through the full UIA -> decision -> screenshot -> OCR path, which
        // also seeds OcrFallbackAnnouncer's per-window scan cache that the position-follow
        // subscription below reads from.
        mouseTracker.Push(window.ScreenPointForLine(0));
        // Waits for the recognized text specifically, not just "some announcement arrived" -
        // RunAndAnnounceAsync also speaks a preliminary "Extracting text..." cue first, which
        // would otherwise satisfy a plain Count > 0 check before the real content is in.
        await WaitUntilAsync(() => speechService.AnnouncedText.Any(t => t.Contains("FIRST LINE", StringComparison.OrdinalIgnoreCase)),
            TimeSpan.FromSeconds(20),
            "hovering the first line never produced an OCR-based announcement");

        var countBeforeMove = speechService.AnnouncedText.Count;

        // Second hover: same UIA element (same Window, same identity) - only the cheap
        // position-follow subscription can possibly announce anything here.
        mouseTracker.Push(window.ScreenPointForLine(1));
        await WaitUntilAsync(() => speechService.AnnouncedText.Count > countBeforeMove, TimeSpan.FromSeconds(5),
            "moving to the second line never announced anything - the position-follow " +
            "subscription likely isn't picking up the cached scan");

        Assert.Contains(speechService.AnnouncedText, t => t.Contains("SECOND LINE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Highlight_follows_individual_OCR_lines_once_a_scan_is_cached()
    {
        var connection = CreateUnstartedConnection();

        using var window = new OpaqueTestWindow("Untestable Chat App", "FIRST LINE HERE", "SECOND LINE HERE");

        var mouseTracker = new FakeMouseTracker();
        var speechService = new RecordingSpeechService();
        var highlightService = new FakeHighlightService();
        var settings = new FakeSettingsService();
        settings.Current.Ocr.KnownProblematicApplications.Add(Process.GetCurrentProcess().ProcessName);

        var services = new ServiceCollection();
        services.AddSingleton<IMouseTracker>(mouseTracker);
        services.AddSingleton(connection);
        services.AddSingleton<IHighlightService>(highlightService);
        services.AddSingleton<IAccessibilitySpeechService>(speechService);
        services.AddSingleton<ISettingsService>(settings);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<WindowEnumerator>();
        services.AddSingleton<IOcrDecisionService, DefaultOcrDecisionService>();
        services.AddSingleton<OcrFallbackAnnouncer>();
        var provider = services.BuildServiceProvider();

        using var tracker = new ElementTracker(provider, NullLogger<ElementTracker>.Instance);

        tracker.IsTracking = true;
        await WaitUntilAsync(() => !mouseTracker.IsPaused, TimeSpan.FromSeconds(2), "did not resume on enable");
        await WaitUntilAsync(() => connection.CurrentState == ConnectionState.Ready, TimeSpan.FromSeconds(20),
            "worker connection never reached Ready");

        // Seeds the per-window OCR scan cache the same way the speech-follow test does. Waits
        // for the recognized text specifically (not just "some announcement arrived") so the
        // scan is actually cached by the time the highlight is checked below - the highlight
        // callback runs continuously from the moment tracking starts (long before OCR
        // completes), so UpdatedLocations already has entries - all of them still the whole
        // window's rect - well before this point.
        mouseTracker.Push(window.ScreenPointForLine(0));
        await WaitUntilAsync(() => speechService.AnnouncedText.Any(t => t.Contains("FIRST LINE", StringComparison.OrdinalIgnoreCase)),
            TimeSpan.FromSeconds(20),
            "hovering the first line never produced an OCR-based announcement");

        // Every mouse sample re-drives the highlight callback (25ms cadence), independent of
        // the throttled speech pipeline - but only once TrackMouseElement's own query pipeline
        // (see WorkerClientExtensions.TrackMouseElement) actually re-queries, and that pipeline
        // applies its own DistinctUntilChanged to raw positions before sampling - so pushing the
        // exact same point again would be filtered out and never reach the highlight callback at
        // all. Alternating by a single pixel keeps the cursor over the same recognized line while
        // still counting as a new position.
        var linePoint = window.ScreenPointForLine(0);
        var alternate = false;
        await WaitUntilAsync(
            () =>
            {
                alternate = !alternate;
                mouseTracker.Push(alternate ? linePoint : new System.Drawing.Point(linePoint.X + 1, linePoint.Y));
                return highlightService.UpdatedLocations[^1].Width < 420;
            },
            TimeSpan.FromSeconds(5),
            "highlight never shrank to a single OCR line's rect");

        var lineRect = highlightService.UpdatedLocations[^1];

        // Never the whole 420x300 test window - proof the box is following the recognized
        // line, not just falling back to the opaque element's full bounding rect.
        Assert.True(lineRect.Width < 420 && lineRect.Height < 300,
            $"expected the highlight to shrink to a single OCR line, but got {lineRect}");
    }

    [Fact]
    public void Disposing_twice_does_not_throw()
    {
        // Production hits this on every ordinary tray "Exit": ElementTracker is a DI singleton
        // that ScreenReaderViewModel both self-registers with IDisposeService AND holds as a
        // field disposed via its own Dispose() - the region navigation library disposes the
        // hosted viewmodel once when the shell clears its region, then IDisposeService.DisposeAll
        // disposes the same ScreenReaderViewModel (and so this same ElementTracker) again. Before
        // the idempotency guard, the second call's _hoveredElements.OnCompleted() threw
        // ObjectDisposedException on the UI thread and broke the shutdown sequence - see the
        // crash reports with Context "UI thread" through DisposeService/MainViewModel/App.OnExit.
        var connection = CreateUnstartedConnection();

        var services = new ServiceCollection();
        services.AddSingleton<IMouseTracker>(new FakeMouseTracker());
        services.AddSingleton(connection);
        services.AddSingleton<IHighlightService>(new FakeHighlightService());
        services.AddSingleton<IAccessibilitySpeechService>(new RecordingSpeechService());
        services.AddSingleton<ISettingsService>(new FakeSettingsService());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<WindowEnumerator>();
        services.AddSingleton<IOcrDecisionService, DefaultOcrDecisionService>();
        services.AddSingleton<OcrFallbackAnnouncer>();
        var provider = services.BuildServiceProvider();

        var tracker = new ElementTracker(provider, NullLogger<ElementTracker>.Instance);

        tracker.Dispose();
        var secondDispose = Record.Exception(() => tracker.Dispose());

        Assert.Null(secondDispose);
    }

    private static WorkerConnection CreateUnstartedConnection()
    {
        var options = new WorkerConnectionOptions
        {
            PipeName = $"peek-worker-test-{Guid.NewGuid():N}",
            WorkerExecutablePath = TestPaths.FindWorkerExecutable(),
            ManageWorkerProcess = true,
            WorkerStartupDelay = TimeSpan.FromMilliseconds(300),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            // See TestWorkerFixture: the 2-second production default is a hover latency
            // budget, not a correctness deadline, and a cold CI runner blows through it on
            // the first UIA call.
            RpcTimeout = TimeSpan.FromSeconds(30),
        };
        return new WorkerConnection(options, NullLoggerFactory.Instance);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string failureMessage)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!condition())
        {
            if (cts.IsCancellationRequested)
                Assert.Fail(failureMessage);
            await Task.Delay(50, CancellationToken.None);
        }
    }

    private static async Task<T> WaitForValueAsync<T>(Func<T?> getValue, TimeSpan timeout, string failureMessage)
        where T : class
    {
        using var cts = new CancellationTokenSource(timeout);
        while (true)
        {
            if (getValue() is { } value) return value;

            if (cts.IsCancellationRequested)
            {
                Assert.Fail(failureMessage);
                throw new UnreachableException();
            }
            await Task.Delay(50, CancellationToken.None);
        }
    }
}
