using Microsoft.Extensions.Logging;

namespace Peek.Worker.Logging;

/// <summary>
/// File-backed <see cref="ILoggerProvider"/> writing to
/// %LOCALAPPDATA%\Peek\logs\{fileNamePrefix}-yyyyMMdd.log, with size-capped rolling and
/// age-based cleanup so logs stay useful for diagnosis without ever growing without bound.
/// </summary>
/// <remarks>
/// Exists because a packaged Release build runs windowed (see Peek.Desktop.csproj /
/// Peek.Worker.csproj) - a Console-subsystem exe launched from a shortcut auto-allocates a
/// visible console window, which is the wall of scrolling log text users mistook for "lots
/// of errors". Without a file sink, going windowed would have silently thrown away every
/// diagnostic an installed app produces.
/// <para>
/// Retention is deliberately bounded on both axes, because an unbounded log is not a
/// theoretical problem here: one stuck-worker bug produced ~5 identical error entries a
/// second, about a megabyte every five minutes, which would have filled a disk over a
/// weekend. Each day keeps at most <see cref="MaxBytesPerDay"/> across a current file and
/// one rolled part, and files older than <see cref="RetentionDays"/> days are deleted - so
/// the whole logs directory is bounded at roughly
/// RetentionDays x MaxBytesPerDay per prefix.
/// </para>
/// <para>
/// Level filtering is left entirely to the owning ILoggingBuilder's SetMinimumLevel rather
/// than duplicated here; installed Release builds log Information and above.
/// </para>
/// </remarks>
public sealed class SimpleFileLoggerProvider : ILoggerProvider
{
    /// <summary>Total budget for one day's logs, across the live file and its one rolled part.</summary>
    public const long MaxBytesPerDay = 10 * 1024 * 1024;

    /// <summary>Days of history kept; anything older is deleted on startup and on each roll.</summary>
    public const int RetentionDays = 3;

    /// <summary>
    /// Roll at half the daily budget and keep one previous part. Rolling (rather than simply
    /// refusing to write past the cap) is what keeps the *most recent* activity - the part
    /// you actually need when diagnosing something that just happened.
    /// </summary>
    private const long RollAtBytes = MaxBytesPerDay / 2;

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _fileNamePrefix;

    private StreamWriter? _writer;
    private string? _currentPath;
    private DateOnly _currentDate;
    private long _bytesWritten;

    public SimpleFileLoggerProvider(string fileNamePrefix)
    {
        _fileNamePrefix = fileNamePrefix;
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Peek", "logs");

        try
        {
            Directory.CreateDirectory(_directory);
            DeleteExpired();
            Open(DateOnly.FromDateTime(DateTime.Now));
        }
        catch
        {
            // Logging must never be why the app fails to start - if the file can't be opened
            // (locked, permission-denied, disk full), every write below just no-ops.
            _writer = null;
        }
    }

    public ILogger CreateLogger(string categoryName) => new SimpleFileLogger(this, categoryName);

    internal void WriteLine(string line)
    {
        lock (_gate)
        {
            try
            {
                RollIfNeeded(line.Length);

                if (_writer is null) return;

                _writer.WriteLine(line);
                _writer.Flush();

                // Close enough for a rolling decision without measuring the file every write.
                _bytesWritten += line.Length + Environment.NewLine.Length;
            }
            catch
            {
            }
        }
    }

    private void RollIfNeeded(int pendingChars)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        // A long-running session crossing midnight should start the new day's file rather
        // than keep appending to yesterday's.
        if (_writer is not null && today != _currentDate)
        {
            Close();
            DeleteExpired();
            Open(today);
            return;
        }

        if (_writer is null || _bytesWritten + pendingChars < RollAtBytes)
            return;

        Close();

        try
        {
            // One rolled part per day, always overwritten: current + .1 keeps the day inside
            // its budget while still retaining more than just the last few lines.
            var rolled = PathFor(_currentDate, part: 1);
            File.Delete(rolled);
            if (_currentPath is not null)
                File.Move(_currentPath, rolled);
        }
        catch
        {
            // If the roll can't happen (file locked by a log viewer, for instance), reopening
            // in append mode below is still better than losing logging altogether.
        }

        Open(_currentDate);
    }

    private void Open(DateOnly date)
    {
        _currentDate = date;
        _currentPath = PathFor(date, part: 0);

        var stream = new FileStream(_currentPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream) { AutoFlush = false };
        _bytesWritten = stream.Length;
    }

    private void Close()
    {
        _writer?.Dispose();
        _writer = null;
        _bytesWritten = 0;
    }

    private string PathFor(DateOnly date, int part) =>
        Path.Combine(
            _directory,
            part == 0
                ? $"{_fileNamePrefix}-{date:yyyyMMdd}.log"
                : $"{_fileNamePrefix}-{date:yyyyMMdd}.{part}.log");

    private void DeleteExpired()
    {
        try
        {
            var cutoff = DateTime.Now.Date.AddDays(-RetentionDays);

            foreach (var file in Directory.EnumerateFiles(_directory, $"{_fileNamePrefix}-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file).Date < cutoff)
                        File.Delete(file);
                }
                catch
                {
                    // A locked or already-removed file is not worth failing over.
                }
            }
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            Close();
        }
    }

    private sealed class SimpleFileLogger(SimpleFileLoggerProvider owner, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {categoryName}: {message}";
            if (exception is not null)
                line += Environment.NewLine + exception;

            owner.WriteLine(line);
        }
    }
}
