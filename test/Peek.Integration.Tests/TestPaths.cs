namespace Peek.Integration.Tests;

internal static class TestPaths
{
    /// <summary>
    /// Finds the repo root by walking up from the test assembly looking for
    /// Peek.slnx, then locates a built Peek.Worker.exe - preferring the shared
    /// flat output folder a whole-solution build produces (bin/net10.0/, see
    /// Peek.Worker.csproj's OutputPath override), falling back to the project's
    /// own per-configuration output directory for a partial/project-only build.
    /// </summary>
    public static string FindWorkerExecutable()
    {
        var overridePath = Environment.GetEnvironmentVariable("Peek_TEST_WORKER_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

        var repoRoot = FindRepoRoot();

        var candidates = new[]
        {
            Path.Combine(repoRoot, "bin", "net10.0", "Peek.Worker.exe"),
            Path.Combine(repoRoot, "src", "worker", "Peek.Worker", "bin", "Debug", "net10.0-windows", "Peek.Worker.exe"),
            Path.Combine(repoRoot, "src", "worker", "Peek.Worker", "bin", "Release", "net10.0-windows", "Peek.Worker.exe"),
        };

        var found = candidates.FirstOrDefault(File.Exists);
        if (found is not null) return found;

        throw new FileNotFoundException(
            "Could not find a built Peek.Worker.exe. Build the solution first " +
            "(dotnet build Peek.slnx), or set Peek_TEST_WORKER_PATH to an explicit path. " +
            "Looked in:\n" + string.Join('\n', candidates));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Peek.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find Peek.slnx by walking up from {AppContext.BaseDirectory}");
    }
}
