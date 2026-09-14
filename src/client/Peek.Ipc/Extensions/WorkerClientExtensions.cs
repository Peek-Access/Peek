using Peek.Worker.Contracts;
using Peek.Worker.Contracts.Automation;
using Peek.Ipc.Worker;
using Peek.Ipc.Connection;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Concurrency;
using ReactiveUI.Primitives.Signals;

namespace Peek.Ipc.Extensions;

public static class WorkerClientExtensions
{
    /// <param name="followInterval">
    /// How often the latest cursor position is turned into a query. This is a <c>Sample</c>
    /// window, not a debounce - see the remarks.
    /// </param>
    /// <remarks>
    /// The sampling operator is the whole feel of the highlight box, and picking the wrong one
    /// is not subtle. <c>Throttle</c>, which in ReactiveUI.Primitives (as in Rx) is
    /// <em>debounce</em>, would be a trap here: it emits only after a quiet period, so while
    /// the mouse keeps moving it emits <em>nothing at all</em>. A window below the mouse
    /// sampling interval would make that pass through unnoticed; anything larger turns it into
    /// a real debounce, and the highlight starts stuttering and trailing the cursor, only
    /// snapping into place when the user stops moving.
    /// <para>
    /// <c>Sample</c> is the operator this actually wanted: emit the most recent position every
    /// window, continuously, while movement is happening. Query volume is still bounded - by
    /// the window, and by <c>Switch</c> below, which keeps at most one query in flight.
    /// HoverOperatorSemanticsTests pins both behaviours.
    /// </para>
    /// </remarks>
    public static IObservable<SemanticElement?> TrackMouseElement(
        this WorkerConnection connection,
        IObservable<(int X, int Y)> mousePositions,
        TimeSpan? followInterval = null,
        ISequencer? scheduler    = null)
    {
        var interval = followInterval ?? TimeSpan.FromMilliseconds(25);
        var sched    = scheduler ?? TaskPoolSequencer.Default;

        // Select+Switch, not SelectMany: SelectMany lets a slow automation.getElementFromPoint
        // call for an OLDER mouse position keep running concurrently with a newer one, and
        // whichever happens to finish last wins - during fast mouse movement this visibly
        // flickers the announced/highlighted element back to a stale one after a newer,
        // faster call already resolved correctly. Switch drops (and, since the CancellationToken
        // handed to Signal.FromAsync is tied to unsubscription, actually cancels) the
        // previous in-flight call the moment a newer mouse position arrives, so only the
        // most recent position's result can ever reach CurrentElement.
        // Every failure here has to become a null element, never an OnError. An Rx sequence
        // that errors is *finished* - and this one is the hover pipeline, so one propagated
        // error permanently stops the highlight and the announcements for the rest of the
        // session, with the app otherwise looking alive. The observed version of that: hover
        // a few elements, the worker dies or the connection drops mid-move, and the highlight
        // box freezes exactly where it was and never moves again.
        //
        // Note the ordering hazard this guards. `connection.Client` throws *synchronously*
        // when the connection is not Ready, and the outer State gate below cannot prevent it:
        // the state can flip between that gate letting a position through and this lambda
        // running. So the guard belongs here, around the property access itself.
        async Task<SemanticElement?> QueryAtAsync((int X, int Y) pos, CancellationToken ct)
        {
            try
            {
                if (connection.CurrentState != ConnectionState.Ready) return null;

                return await connection.Client.Automation
                    .GetElementFromPointAsync(pos.X, pos.Y, ct).ConfigureAwait(false);
            }
            catch
            {
                // Cancellation (a newer mouse position superseded this one - the common case),
                // an RPC timeout, or a connection that went away mid-call. None of them are
                // worth ending the stream over; the next mouse move just tries again.
                return null;
            }
        }

        IObservable<SemanticElement?> QueryObservable() =>
            mousePositions
                .DistinctUntilChanged()
                .Sample(interval, sched)
                .Select(pos => Signal.FromAsync(ct => QueryAtAsync(pos, ct)))
                .Switch();

        return connection.State
            .Select(state => state == ConnectionState.Ready)
            .DistinctUntilChanged()
            .Select(isReady =>
                isReady
                    ? QueryObservable()
                    : Signal.Return<SemanticElement?>(null))
            .Switch();
    }

    public static IObservable<WorkerStatus?> PollStatus(
        this WorkerConnection connection,
        TimeSpan interval,
        ISequencer? scheduler = null)
    {
        var sched = scheduler ?? TaskPoolSequencer.Default;

        return Signal
            .Interval(interval, sched)
            .Where(_ => connection.CurrentState == ConnectionState.Ready)
            .SelectMany(_ => Signal.FromAsync(async ct =>
            {
                try
                {
                    return (WorkerStatus?)await connection.Client.Automation
                        .GetStatusAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    return null;
                }
            }));
    }

    public static IObservable<bool> IsReady(this WorkerConnection connection) =>
        connection.State
            .Select(s => s == ConnectionState.Ready)
            .DistinctUntilChanged();
}
