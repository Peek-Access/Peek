using Peek.Core.ViewModels;
using Xunit;

namespace Peek.Core.Tests.Ipc;

public sealed class DependencyVersionsTests
{
    [Fact]
    public void Missing_dependencies_do_not_break_about_window()
    {
        var libraries = DependencyVersions.GetLibraries(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.Equal(8, libraries.Count);
        Assert.Contains($".NET {Environment.Version}", libraries);
        Assert.Contains("VLC —", libraries);
        Assert.Contains("PiperSharp —", libraries);
    }

    [Theory]
    [InlineData("")]
    [InlineData("worker")]
    [InlineData("custom-worker")]
    public void Worker_versions_are_read_without_loading_the_assembly(string directory)
    {
        var root = Path.Combine(Path.GetTempPath(), "peek-version-test-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, directory);
        Directory.CreateDirectory(target);
        try
        {
            var source = typeof(DependencyVersions).Assembly.Location;
            File.Copy(source, Path.Combine(target, "PiperSharp.dll"));
            var workerPath = directory == "custom-worker" ? Path.Combine(target, "Peek.Worker.exe") : null;
            var libraries = DependencyVersions.GetLibraries(root, workerPath);
            var expected = DependencyVersions.ReadVersion(source);
            Assert.NotEqual("—", expected);
            Assert.DoesNotContain("+", expected);
            Assert.Contains($"PiperSharp {expected}", libraries);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
