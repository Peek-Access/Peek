using Avalonia.Data.Converters;
using Peek.Ipc.Connection;
using System;
using System.Globalization;

namespace Peek.UI.Converters;

/// <summary>True only for ConnectionState.Ready - drives the sidebar footer's status dot/label.</summary>
public class ConnectionStateIsReadyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ConnectionState.Ready;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>Human-readable label for the sidebar footer's worker-status line.</summary>
public class ConnectionStateToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            ConnectionState.Ready => "Worker connected",
            ConnectionState.StartingWorker => "Starting worker…",
            ConnectionState.Connecting => "Connecting…",
            ConnectionState.Reconnecting => "Reconnecting…",
            // Not a dead end - the watchdog starts a fresh recovery cycle within its interval,
            // so say so rather than leaving the user staring at an unexplained failure.
            ConnectionState.Faulted => "Worker unavailable - retrying…",
            ConnectionState.Stopped => "Worker stopped",
            _ => "Worker not running",
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
