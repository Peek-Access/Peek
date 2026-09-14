using System;
using System.Runtime.Versioning;
using System.Threading;
using Avalonia;
using Peek.UI;
using ReactiveUI.Avalonia;

namespace Peek.Desktop;

[SupportedOSPlatform("windows7.0")]
sealed class Program
{
    // Unqualified (no "Global\"/"Local\" prefix) so this is scoped to the current user
    // session, matching "one Peek per desktop session" rather than one per machine.
    private const string SingleInstanceMutexName = "Peek.Desktop.SingleInstance";

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Held for the whole process lifetime (Main blocks inside
        // StartWithClassicDesktopLifetime until the app exits, so disposal here happens at
        // the right time). A second launch bails out before touching Avalonia, DI, or
        // spawning its own Peek.Worker.exe - the latter matters beyond UX: two workers
        // would fight over the same named pipe ("peek-worker"), which is what produced the
        // "All pipe instances are busy" error storm seen earlier while testing multi-instance
        // scenarios.
        using var singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            SingleInstance.ActivateExistingInstance();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI(_ =>{});
}
