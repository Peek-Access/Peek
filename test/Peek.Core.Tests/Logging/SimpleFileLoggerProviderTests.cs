using Microsoft.Extensions.Logging;
using Peek.Core.Logging;
using Xunit;

namespace Peek.Core.Tests.Logging;

/// <summary>
/// The log is the only diagnostic an installed build leaves behind, and it has to stay
/// bounded: a single stuck-worker bug was observed producing ~5 identical error entries a
/// second - roughly a megabyte every five minutes - which without rolling would fill a disk
/// over a weekend.
/// </summary>
public sealed class SimpleFileLoggerProviderTests : IDisposable
{
    private readonly string _logDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Peek", "logs");

    private readonly string _prefix = $"peek-test-{Guid.NewGuid():N}";

    [Fact]
    public void Writes_to_a_dated_file_under_the_peek_logs_directory()
    {
        using (var provider = new SimpleFileLoggerProvider(_prefix))
        {
            provider.CreateLogger("Test").LogInformation("hello");
        }

        var file = Path.Combine(_logDirectory, $"{_prefix}-{DateTime.Now:yyyyMMdd}.log");
        Assert.True(File.Exists(file));
        Assert.Contains("hello", ReadShared(file));
    }

    /// <summary>
    /// The provider holds the file open for the life of the app, so a plain
    /// <see cref="File.ReadAllText(string)"/> (which asks for FileShare.Read) is refused
    /// while it's alive. Reading with FileShare.ReadWrite is also what a user tailing the
    /// log in a viewer has to do.
    /// </summary>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Keeps_a_day_within_its_size_budget_by_rolling()
    {
        using (var provider = new SimpleFileLoggerProvider(_prefix))
        {
            var logger = provider.CreateLogger("Test");

            // Comfortably past the daily budget - without rolling this would simply be a
            // >10MB file.
            var padding = new string('x', 4096);
            for (var i = 0; i < 4000; i++)
                logger.LogInformation("{Index} {Padding}", i, padding);
        }

        var total = Directory.EnumerateFiles(_logDirectory, $"{_prefix}-*.log")
            .Sum(f => new FileInfo(f).Length);

        Assert.True(
            total <= SimpleFileLoggerProvider.MaxBytesPerDay,
            $"a single day's logs totalled {total} bytes, over the {SimpleFileLoggerProvider.MaxBytesPerDay} budget");
    }

    [Fact]
    public void Rolling_keeps_the_most_recent_entries_rather_than_the_oldest()
    {
        // Diagnosing something that just happened needs the *end* of the log, so a full
        // budget must not mean "stop recording".
        using (var provider = new SimpleFileLoggerProvider(_prefix))
        {
            var logger = provider.CreateLogger("Test");
            var padding = new string('x', 4096);

            for (var i = 0; i < 3000; i++)
                logger.LogInformation("{Padding}", padding);

            logger.LogInformation("THE-LAST-THING-THAT-HAPPENED");
        }

        var current = Path.Combine(_logDirectory, $"{_prefix}-{DateTime.Now:yyyyMMdd}.log");
        Assert.Contains("THE-LAST-THING-THAT-HAPPENED", ReadShared(current));
    }

    [Fact]
    public void Deletes_logs_older_than_the_retention_window()
    {
        var stale = Path.Combine(_logDirectory, $"{_prefix}-20200101.log");
        Directory.CreateDirectory(_logDirectory);
        File.WriteAllText(stale, "ancient history");
        File.SetLastWriteTime(stale, DateTime.Now.AddDays(-(SimpleFileLoggerProvider.RetentionDays + 1)));

        using var provider = new SimpleFileLoggerProvider(_prefix);

        Assert.False(File.Exists(stale));
    }

    [Fact]
    public void Keeps_logs_inside_the_retention_window()
    {
        var recent = Path.Combine(_logDirectory, $"{_prefix}-20200102.log");
        Directory.CreateDirectory(_logDirectory);
        File.WriteAllText(recent, "still useful");
        File.SetLastWriteTime(recent, DateTime.Now.AddDays(-1));

        using var provider = new SimpleFileLoggerProvider(_prefix);

        Assert.True(File.Exists(recent));
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_logDirectory, $"{_prefix}-*.log"))
        {
            try { File.Delete(file); }
            catch { /* best effort test cleanup */ }
        }
    }
}
