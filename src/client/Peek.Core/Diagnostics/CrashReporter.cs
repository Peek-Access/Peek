using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Peek.Core.Diagnostics;

/// <summary>
/// Writes a self-contained crash report to <c>%LOCALAPPDATA%\Peek\crashes\</c> whenever an
/// unhandled exception reaches one of the app's last-chance handlers.
/// </summary>
/// <remarks>
/// Deliberately local-only, with no network component and no opt-in prompt to get wrong:
/// Peek reads the contents of other people's screens, so anything it might transmit is
/// potentially the most sensitive data on the machine. A file the user can find, read in
/// full, and choose to attach to an issue keeps the "nothing leaves your machine" promise
/// literally true while still making a field failure diagnosable - today the only trace is a
/// line in a rolling log that gets buried within minutes of normal use.
/// <para>
/// Every write is best-effort: a crash reporter that throws while reporting a crash turns a
/// recoverable failure into a lost one.
/// </para>
/// </remarks>
public static class CrashReporter
{
    public static string CrashDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Peek", "crashes");

    /// <summary>
    /// Writes a report and returns its path, or null if it couldn't be written.
    /// <paramref name="context"/> says which handler caught it (UI thread, background task,
    /// AppDomain), which is usually the first clue about what was running at the time.
    /// </summary>
    public static string? Write(Exception? exception, string context)
    {
        try
        {
            Directory.CreateDirectory(CrashDirectory);

            var path = Path.Combine(
                CrashDirectory,
                $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");

            File.WriteAllText(path, BuildReport(exception, context), Encoding.UTF8);
            PruneOldReports();
            return path;
        }
        catch
        {
            // Nothing useful to do here - the logger is the fallback, and if the disk is
            // gone, so is that. Never let this path throw.
            return null;
        }
    }

    private static string BuildReport(Exception? exception, string context)
    {
        var assembly = Assembly.GetEntryAssembly();
        var sb = new StringBuilder();

        sb.AppendLine("Peek crash report");
        sb.AppendLine("=================");
        sb.AppendLine();
        sb.AppendLine($"When:      {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Context:   {context}");
        sb.AppendLine($"Version:   {assembly?.GetName().Version?.ToString() ?? "unknown"}");
        sb.AppendLine($"App:       {assembly?.GetName().Name ?? "unknown"}");
        sb.AppendLine($"OS:        {RuntimeInformation.OSDescription}");
        sb.AppendLine($"Runtime:   {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Arch:      {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine();
        sb.AppendLine("Exception");
        sb.AppendLine("---------");
        sb.AppendLine(exception?.ToString() ?? "(no exception object was supplied)");
        sb.AppendLine();
        sb.AppendLine(
            "This file stays on your machine. Nothing here is sent anywhere. It contains no " +
            "screen contents, window titles, or announcement text - only the failure itself. " +
            "Attach it to a bug report at https://github.com/Peek-Access/Peek/issues if you " +
            "want it looked at.");

        return sb.ToString();
    }

    /// <summary>Keeps the newest 20 reports - enough to see a pattern, not enough to accumulate unbounded.</summary>
    private static void PruneOldReports()
    {
        const int keep = 20;

        var reports = new DirectoryInfo(CrashDirectory)
            .GetFiles("crash-*.txt")
            .OrderByDescending(f => f.CreationTimeUtc)
            .Skip(keep);

        foreach (var report in reports)
        {
            try { report.Delete(); }
            catch { /* a locked/vanished file is not worth failing over */ }
        }
    }
}
