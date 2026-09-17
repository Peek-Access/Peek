using System;
using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Platform;

[assembly: AvaloniaTestApplication(typeof(Peek.UI.Tests.UnitTestAppBuilder))]

namespace Peek.UI.Tests;

// Same shape as ProDataGrid's own TestAppBuilder (github.com/NeverMorewd/ProDataGrid) - the
// font-manager reflection dance works around headless mode otherwise failing to resolve one
// on this Avalonia version, unrelated to anything Peek-specific.
internal sealed class UnitTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var options = new AvaloniaHeadlessPlatformOptions
        {
            UseHeadlessDrawing = true
        };

        return AppBuilder.Configure<UnitTestApp>()
            .UseHeadless(options)
            .AfterPlatformServicesSetup(_ => RegisterHeadlessFontManager());
    }

    private static void RegisterHeadlessFontManager()
    {
        var baseAssembly = typeof(AvaloniaObject).Assembly;
        var locatorType = baseAssembly.GetType("Avalonia.AvaloniaLocator");
        if (locatorType == null) return;

        var currentMutableProperty = locatorType.GetProperty("CurrentMutable", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        var currentMutable = currentMutableProperty?.GetValue(null);
        if (currentMutable == null) return;

        var fontManager = CreateHeadlessFontManager();
        if (fontManager == null) return;

        var bindMethod = locatorType.GetMethod("Bind", BindingFlags.Public | BindingFlags.Instance);
        var bindGeneric = bindMethod?.MakeGenericMethod(typeof(IFontManagerImpl));
        var helper = bindGeneric?.Invoke(currentMutable, null);
        if (helper == null) return;

        try
        {
            var toConstant = helper.GetType().GetMethod("ToConstant", BindingFlags.Public | BindingFlags.Instance);
            toConstant?.Invoke(helper, new[] { fontManager });
        }
        catch
        {
        }
    }

    private static object? CreateHeadlessFontManager()
    {
        var assembly = typeof(AvaloniaHeadlessPlatformOptions).Assembly;
        var fontManagerType = assembly.GetType("Avalonia.Headless.HeadlessFontManagerWithMultipleSystemFontsStub")
            ?? assembly.GetType("Avalonia.Headless.HeadlessFontManagerStub");
        if (fontManagerType == null) return null;

        if (fontManagerType.Name.Contains("WithMultiple", StringComparison.Ordinal))
            return Activator.CreateInstance(fontManagerType, new object?[] { new[] { "Default" }, "Default" });

        return Activator.CreateInstance(fontManagerType, new object?[] { "Default" });
    }
}

internal sealed class UnitTestApp : Application
{
    public override void Initialize()
    {
    }
}
