namespace Peek.Ipc.Transport;

public interface IPipeTransport : IDisposable
{
    /// <summary>Hot observable of raw JSON lines arriving from the worker.</summary>
    IObservable<string> ReceivedLines { get; }

    /// <summary>Current connection state of the transport.</summary>
    IObservable<TransportState> State { get; }

    /// <summary>Send a single line (must not contain newlines).</summary>
    Task SendLineAsync(string line, CancellationToken ct = default);

    /// <summary>Connect to the named pipe. Idempotent if already connected.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Disconnect and release the pipe handle.</summary>
    Task DisconnectAsync();
}

public enum TransportState
{
    Disconnected,
    Connecting,
    Connected,
    Faulted
}
