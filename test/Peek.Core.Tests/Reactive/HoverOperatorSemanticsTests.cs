using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Concurrency;
using ReactiveUI.Primitives.Signals;
using Xunit;

namespace Peek.Core.Tests.Reactive;

/// <summary>
/// Pins what <c>Throttle</c> and <c>Sample</c> actually do in ReactiveUI.Primitives, because
/// the hover pipeline's feel depends entirely on the difference and the package ships no XML
/// docs for either.
/// </summary>
/// <remarks>
/// Pins the distinction because the two operators are easy to swap by mistake with
/// consequences that only show up as feel, not a compile error: <c>Throttle</c> is
/// <em>debounce</em> - emitting only after a quiet period, so while the mouse keeps moving it
/// emits nothing at all - and using it for the mouse-query path makes the highlight box
/// visibly stutter and lag behind the cursor once the window sits above the mouse sampling
/// interval.
/// <para>
/// The two operators are both correct, for different jobs: <c>Sample</c> for "follow the
/// cursor continuously" (highlight), <c>Throttle</c> for "act once the user has settled"
/// (speak). These tests state which is which so the next change picks deliberately.
/// </para>
/// <para>
/// Driven by a <see cref="VirtualClock"/> rather than by <c>Task.Delay</c>: real time lets a
/// loaded build agent stretch a 20ms delay past a 50ms debounce window, at which point the
/// operator fires exactly as documented and the test reports a defect that does not exist.
/// Virtual time takes the scheduler out of the assertion: these test the operators, not the
/// machine they run on.
/// </para>
/// </remarks>
public sealed class HoverOperatorSemanticsTests
{
    /// <summary>Mouse samples arrive about this often; the hover pipeline is built around it.</summary>
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(20);

    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(50);

    private const int Pushes = 20;

    [Fact]
    public void Throttle_emits_nothing_while_values_keep_arriving()
    {
        // The defect, stated as a test: a continuously-moving mouse produces no output at all.
        using var run = Collect(useSample: false);

        Assert.Empty(run.Received);
    }

    [Fact]
    public void Throttle_emits_the_final_value_once_the_values_stop()
    {
        using var run = Collect(useSample: false);
        Assert.Empty(run.Received);

        // The mouse stops: exactly one emission, carrying where it ended up - the "act once
        // the user has settled" shape the announcement path wants.
        run.Clock.AdvanceBy(Window + TimeSpan.FromMilliseconds(1));

        Assert.Single(run.Received);
        Assert.Equal(Pushes - 1, run.Received[0]);
    }

    [Fact]
    public void Sample_keeps_emitting_while_values_keep_arriving()
    {
        // What Sample must guarantee: the highlight keeps getting positions mid-movement.
        // 20 pushes 20ms apart against a 50ms window is roughly one emission per two-and-a-bit
        // pushes.
        using var run = Collect(useSample: true);

        Assert.True(run.Received.Count >= 5, $"Expected repeated emissions during movement; got {run.Received.Count}.");
    }

    [Fact]
    public void Sample_emits_the_most_recent_value_not_a_backlog()
    {
        // Matters for a highlight box: an emission carrying a stale cursor position would draw
        // the rectangle where the mouse *was*, which looks the same as lagging.
        using var run = Collect(useSample: true);

        Assert.NotEmpty(run.Received);
        for (var i = 1; i < run.Received.Count; i++)
            Assert.True(run.Received[i] > run.Received[i - 1], "Sampled values must advance monotonically");

        // Within one window of the newest value pushed, i.e. current rather than replayed.
        Assert.True(run.Received[^1] >= Pushes - 4,
            $"Last sampled value {run.Received[^1]} lags the last pushed value {Pushes - 1}");
    }

    /// <summary>
    /// Pushes an incrementing value every <see cref="SampleInterval"/> of <em>virtual</em> time
    /// - a mouse being moved steadily - and returns whatever the operator let through, plus the
    /// clock, so a test can then let time run on to represent the mouse stopping.
    /// </summary>
    private static Run Collect(bool useSample)
    {
        var clock = new VirtualClock();
        var source = new Signal<int>();
        var received = new List<int>();

        var observable = useSample
            ? source.AsObservable().Sample(Window, clock)
            : source.AsObservable().Throttle(Window, clock);

        // The subscription is handed back rather than disposed here: a test that wants to
        // represent the mouse *stopping* advances the clock after this returns, and there has
        // to still be a subscriber when it does.
        var subscription = observable.Subscribe(
            onNext: value => received.Add(value),
            onError: _ => { });

        for (var i = 0; i < Pushes; i++)
        {
            source.OnNext(i);
            clock.AdvanceBy(SampleInterval);
        }

        return new Run(received, clock, subscription);
    }

    private sealed record Run(List<int> Received, VirtualClock Clock, IDisposable Subscription) : IDisposable
    {
        public void Dispose() => Subscription.Dispose();
    }
}
