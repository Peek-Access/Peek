using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Peek.Worker.Contracts.SystemMonitor;

namespace Peek.Worker.SystemMonitor.Windows;

/// <summary>
/// Reads the same registry data Control Panel's "Programs and Features" does, and controls
/// running processes via System.Diagnostics.Process - backs the App Monitor and Process
/// Monitor dock views. A resolved launch path is never sent to the client: apps are
/// launched (and processes killed/revealed) here, by key/pid, so the client only ever deals
/// in opaque identifiers.
/// </summary>
[SupportedOSPlatform("windows7.0")]
public sealed class WindowsSystemMonitorService : ISystemMonitorService
{
    private static readonly (RegistryKey Hive, string Path)[] UninstallRoots =
    [
        (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
    ];

    private readonly ILogger<WindowsSystemMonitorService> _logger;

    // Resolved launch targets never leave this process - the client only ever sees the
    // registry key + CanLaunch, and asks to launch "by key" (see LaunchAppAsync). Rebuilt
    // fresh on every EnumerateAppsAsync so a launch always targets what was last shown.
    private readonly Dictionary<string, string> _launchTargetsByKey = new(StringComparer.OrdinalIgnoreCase);

    public WindowsSystemMonitorService(ILogger<WindowsSystemMonitorService> logger) => _logger = logger;

    public Task<IReadOnlyList<InstalledAppDto>> EnumerateAppsAsync(CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var results = new List<InstalledAppDto>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _launchTargetsByKey.Clear();

            foreach (var (hive, path) in UninstallRoots)
            {
                ct.ThrowIfCancellationRequested();
                ReadUninstallRoot(hive, path, results, seenNames);
            }

            return (IReadOnlyList<InstalledAppDto>)results;
        }, ct);

    private void ReadUninstallRoot(RegistryKey hive, string path, List<InstalledAppDto> results, HashSet<string> seenNames)
    {
        using var root = hive.OpenSubKey(path);
        if (root is null) return;

        foreach (var subKeyName in root.GetSubKeyNames())
        {
            try
            {
                using var subKey = root.OpenSubKey(subKeyName);
                if (subKey is null) continue;

                var name = subKey.GetValue("DisplayName") as string;
                // Entries with no DisplayName are almost always hotfixes/components, not
                // something a person would recognize as "an app" - skip rather than show
                // dozens of blank rows. SystemComponent=1 is the registry's own way of
                // marking framework/runtime pieces as hidden from Programs and Features.
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (subKey.GetValue("SystemComponent") is int sc && sc == 1) continue;

                var version = subKey.GetValue("DisplayVersion") as string ?? string.Empty;
                // Same app can legitimately appear under both 64-bit and WOW6432Node roots
                // for some installers - dedupe by name+version rather than registry key
                // path (which always differs between hives).
                var dedupeKey = $"{name}\u0000{version}";
                if (!seenNames.Add(dedupeKey)) continue;

                var installLocation = subKey.GetValue("InstallLocation") as string;
                var displayIcon = subKey.GetValue("DisplayIcon") as string;
                var estimatedSizeKb = subKey.GetValue("EstimatedSize") is int size && size > 0 ? (long?)size : null;
                var launchTarget = ResolveLaunchTarget(displayIcon, installLocation, name);

                // Registry key names aren't guaranteed unique across the three roots, and
                // this dictionary is the source of truth LaunchAppAsync looks up by - key
                // the public "Key" DTO field by name+version too, so it's stable and
                // matches what EnumerateAppsAsync just deduped on.
                var publicKey = dedupeKey;
                if (launchTarget is not null)
                    _launchTargetsByKey[publicKey] = launchTarget;

                results.Add(new InstalledAppDto
                {
                    Key = publicKey,
                    Name = name,
                    Version = version,
                    Publisher = subKey.GetValue("Publisher") as string ?? string.Empty,
                    EstimatedSizeKb = estimatedSizeKb,
                    CanLaunch = launchTarget is not null,
                });
            }
            catch (Exception ex)
            {
                // A single malformed/inaccessible registry entry shouldn't blank the whole
                // list - matches WindowsAutomationService's per-item defensive try/catch.
                _logger.LogTrace(ex, "Failed to read Uninstall registry entry {Key}", subKeyName);
            }
        }
    }

    /// <summary>
    /// Best-effort: the Uninstall registry doesn't reliably record "here's the exe to run" -
    /// DisplayIcon usually does (often "C:\...\App.exe,0" - an icon reference, not always an
    /// icon *file*), so it's tried first. Falling back to the single .exe in InstallLocation
    /// covers installers that only ever set DisplayIcon to a generic .ico. Returns null
    /// (launch disabled for that row) rather than guessing wrong and launching nothing/the
    /// wrong thing.
    /// </summary>
    private static string? ResolveLaunchTarget(string? displayIcon, string? installLocation, string name)
    {
        if (!string.IsNullOrWhiteSpace(displayIcon))
        {
            var iconPath = displayIcon.Split(',')[0].Trim('"', ' ');
            if (iconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(iconPath))
                return iconPath;
        }

        if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
        {
            try
            {
                var exeFiles = Directory.GetFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly);
                if (exeFiles.Length == 1) return exeFiles[0];

                // Prefer an exe whose name closely matches the app's display name over an
                // arbitrary pick among several (installer/uninstaller helper exes are
                // common siblings of the real one).
                var byName = exeFiles.FirstOrDefault(f =>
                    Path.GetFileNameWithoutExtension(f).Contains(name, StringComparison.OrdinalIgnoreCase));
                if (byName is not null) return byName;
            }
            catch (IOException)
            {
                // Directory enumeration can race with an uninstall in progress - not fatal, just no launch target.
            }
        }

        return null;
    }

    public Task<ActionResult> LaunchAppAsync(string key, CancellationToken ct = default)
    {
        if (!_launchTargetsByKey.TryGetValue(key, out var path))
            return Task.FromResult(new ActionResult { Success = false, Error = "No launchable executable found for this app." });

        try
        {
            using var process = Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path),
            });
            return Task.FromResult(new ActionResult { Success = process is not null });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ActionResult { Success = false, Error = ex.Message });
        }
    }

    public Task<IReadOnlyList<ProcessDto>> EnumerateProcessesAsync(bool includeSystemProcesses, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var results = new List<ProcessDto>();

            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var dto = Snapshot(process);
                        if (dto.IsSystemProcess && !includeSystemProcesses) continue;
                        results.Add(dto);
                    }
                    catch (Exception ex)
                    {
                        // Most processes not owned by this user throw Win32Exception (access
                        // denied) on MainModule/MainWindowTitle access - routine, not a bug.
                        _logger.LogTrace(ex, "Failed to snapshot process {Pid}", process.Id);
                    }
                }
            }

            return (IReadOnlyList<ProcessDto>)results;
        }, ct);

    private static ProcessDto Snapshot(Process process)
    {
        string description = string.Empty;

        // MainModule access is the expensive/fragile part - wrapped separately so a process
        // we can't introspect still shows up with at least its name/id/session, rather than
        // being dropped from the list entirely.
        try
        {
            description = process.MainModule?.FileVersionInfo.FileDescription ?? string.Empty;
        }
        catch (Exception)
        {
            // Access denied (protected/elevated process) or the process exited mid-read - leave description blank.
        }

        long memoryBytes;
        int sessionId;
        try
        {
            memoryBytes = process.PrivateMemorySize64;
            sessionId = process.SessionId;
        }
        catch (Exception)
        {
            memoryBytes = 0;
            sessionId = -1;
        }

        return new ProcessDto
        {
            Id = process.Id,
            Name = process.ProcessName,
            MemoryBytes = memoryBytes,
            Description = description,
            IsSystemProcess = sessionId == 0,
        };
    }

    public Task<ActionResult> KillProcessAsync(int processId, CancellationToken ct = default)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.SessionId == 0)
            {
                // Server-side refusal regardless of what the client asked for - the client
                // UI already hides this action for a system process, but a stale row
                // (pid reused since the list was fetched) shouldn't let a race sneak one
                // through.
                return Task.FromResult(new ActionResult { Success = false, Error = "Ending system processes is not supported." });
            }

            process.Kill();
            return Task.FromResult(new ActionResult { Success = true });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ActionResult { Success = false, Error = ex.Message });
        }
    }

    public Task<ActionResult> OpenProcessFolderAsync(int processId, CancellationToken ct = default)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var path = process.MainModule?.FileName;
            if (string.IsNullOrEmpty(path))
                return Task.FromResult(new ActionResult { Success = false, Error = "This process's file location isn't accessible." });

            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            return Task.FromResult(new ActionResult { Success = true });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ActionResult { Success = false, Error = ex.Message });
        }
    }
}
