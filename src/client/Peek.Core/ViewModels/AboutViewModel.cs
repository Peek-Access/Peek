using System.Diagnostics;
using ReactiveUI.SourceGenerators;

namespace Peek.Core.ViewModels;

/// <summary>Backs the About window.</summary>
public partial class AboutViewModel : ViewModelBase
{
    public string AppName => "Peek";
    public string Tagline => "A screen reader for Windows";
    public string Version => AppInfo.Version;
    public string Copyright => AppInfo.Copyright;
    public string LicenseName => "GNU General Public License v3.0";
    public string RepoUrl => "https://github.com/Peek-Access/Peek";
    public string LicenseUrl => "https://www.gnu.org/licenses/gpl-3.0.en.html";

    /// <summary>Not exhaustive - the libraries most worth crediting by name, not every transitive package.</summary>
    public IReadOnlyList<string> ThirdPartyLibraries { get; } =
    [
        "Avalonia UI",
        "Pipboy.Avalonia",
        "AsyncNavigation",
        "ReactiveUI",
        "LibVLCSharp",
        "Irihi.Lingua",
        "Microsoft.Windows.CsWin32",
        "PiperSharp",
        "Sdcb.SimdPaddleOCR",
    ];

    [ReactiveCommand]
    private void OpenRepo() => OpenUrl(RepoUrl);

    [ReactiveCommand]
    private void OpenLicense() => OpenUrl(LicenseUrl);

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Best-effort - no default browser configured, sandboxed environment, etc.
            // Nothing useful to recover into; the window still shows the URL as text.
        }
    }
}
