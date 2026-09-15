using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Peek.Core.ViewModels;

/// <summary>Reads installed binary metadata without loading worker assemblies into the UI.</summary>
public static class DependencyVersions
{
    public static IReadOnlyList<string> GetLibraries(string baseDirectory, string? workerExecutablePath = null)
    {
        var workerDirectory = string.IsNullOrWhiteSpace(workerExecutablePath)
            ? Path.Combine(baseDirectory, "worker")
            : Path.GetDirectoryName(Path.GetFullPath(workerExecutablePath, baseDirectory))!;

        string Client(string dll) => ReadVersion(Path.Combine(baseDirectory, dll));
        string Worker(string dll)
        {
            var deployed = Path.Combine(workerDirectory, dll);
            return ReadVersion(File.Exists(deployed) ? deployed : Path.Combine(baseDirectory, dll));
        }

        var architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        var vlcPath = Path.Combine(baseDirectory, "libvlc", $"win-{architecture}", "libvlc.dll");
        if (!File.Exists(vlcPath)) vlcPath = Path.Combine(baseDirectory, "libvlc.dll");

        return
        [
            $"Avalonia {Client("Avalonia.Base.dll")}",
            $".NET {Environment.Version}",
            $"VLC {ReadVersion(vlcPath)}",
            $"Pipboy.Avalonia {Client("Pipboy.Avalonia.dll")}",
            $"AsyncNavigation {Client("AsyncNavigation.dll")}",
            $"Irihi.Lingua {Client("Irihi.Lingua.Core.dll")}",
            $"Sdcb.SimdPaddleOCR {Worker("Sdcb.SimdPaddleOCR.dll")}",
            $"PiperSharp {Worker("PiperSharp.dll")}",
        ];
    }

    public static string ReadVersion(string path)
    {
        try
        {
            if (!File.Exists(path)) return "—";
            var info = FileVersionInfo.GetVersionInfo(path);
            var version = string.IsNullOrWhiteSpace(info.ProductVersion) ? info.FileVersion : info.ProductVersion;
            // Keep prerelease labels; omit build metadata (usually the commit hash).
            return string.IsNullOrWhiteSpace(version) ? "—" : version.Split('+', 2)[0].Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return "—";
        }
    }
}
