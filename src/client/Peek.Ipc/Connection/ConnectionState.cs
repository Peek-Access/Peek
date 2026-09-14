
namespace Peek.Ipc.Connection;

public enum ConnectionState
{
    /// Not yet started or cleanly shut down.
    Idle,

    /// Starting the worker process.
    StartingWorker,

    /// Worker is running, pipe connection in progress.
    Connecting,

    /// Pipe connected, RPC channel ready.
    Ready,

    /// Connection lost; waiting before next reconnect attempt.
    Reconnecting,

    /// <summary>
    /// A recovery attempt ran and did not get back to <see cref="Ready"/>. Distinct from
    /// <see cref="Reconnecting"/> on purpose: recovery is no longer in flight, so this is the
    /// state the watchdog looks for to start the next attempt. Before this existed, a failed
    /// recovery left the connection sitting in <see cref="Reconnecting"/> forever and nothing
    /// ever retried it - see WorkerConnection.TriggerReconnectAsync.
    /// </summary>
    Faulted,

    /// Permanently stopped (Dispose called).
    Stopped,
}

public sealed class ConnectionStateChangedEventArgs : EventArgs
{
    public ConnectionState Previous { get; init; }
    public ConnectionState Current  { get; init; }
    public Exception?      Reason   { get; init; }
}
