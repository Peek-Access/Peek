using System.Runtime.CompilerServices;
using ReactiveUI.Builder;
using Splat;

namespace Peek.Integration.Tests;

/// <summary>
/// This test host never runs through Avalonia's <c>AppBuilder.UseReactiveUI()</c> (that's
/// only wired up in Peek.Desktop's Program.cs), so ReactiveUI's static state - which
/// ElementTracker relies on via [Reactive]/ReactiveObject - is never initialized. Newer
/// ReactiveUI versions require the explicit builder pattern instead of implicit
/// first-use initialization; without this, constructing ElementTracker throws
/// "ReactiveUI has not been initialized" from ReactiveNotifyPropertyChangedMixins's cctor.
/// </summary>
internal static class ReactiveUIInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        new ReactiveUIBuilder(Locator.CurrentMutable, Locator.Current)
            .WithPlatformServices()
            .WithCoreServices()
            .BuildApp();
    }
}
