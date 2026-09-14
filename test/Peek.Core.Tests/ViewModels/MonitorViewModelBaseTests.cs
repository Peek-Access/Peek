using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Peek.Core.ViewModels;
using ReactiveUI.Builder;
using Splat;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using NullLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger;
using Xunit;

namespace Peek.Core.Tests.ViewModels;

/// <summary>
/// ReactiveUI's static state is normally initialized by Avalonia's AppBuilder.UseReactiveUI()
/// in Peek.Desktop's Program.cs, which a plain unit-test host never runs - without this,
/// constructing anything deriving from ReactiveObject throws "ReactiveUI has not been
/// initialized". Mirrors the integration suite's own initializer.
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

/// <summary>
/// The guarded-load wrapper every monitor view uses. Worth pinning because its whole job is
/// what happens when the worker call fails: the list stays empty, the spinner has to stop
/// anyway, and the failure must not escape into the UI as an unhandled exception.
/// </summary>
public sealed class MonitorViewModelBaseTests
{
    [Fact]
    public async Task Clears_the_loading_flag_after_a_successful_load()
    {
        var vm = new TestMonitorViewModel();

        await vm.RunAsync(() => Task.CompletedTask);

        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task Sets_the_loading_flag_while_the_load_is_in_flight()
    {
        var vm = new TestMonitorViewModel();
        var wasLoadingDuringLoad = false;

        await vm.RunAsync(() =>
        {
            wasLoadingDuringLoad = vm.IsLoading;
            return Task.CompletedTask;
        });

        Assert.True(wasLoadingDuringLoad);
    }

    [Fact]
    public async Task A_failing_load_does_not_throw_and_still_clears_the_loading_flag()
    {
        // A worker that's restarting, timing out, or has died mid-call must leave the view
        // usable rather than stuck behind a spinner that never goes away.
        var vm = new TestMonitorViewModel();

        await vm.RunAsync(() => throw new InvalidOperationException("worker unavailable"));

        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task A_failing_load_is_logged_rather_than_swallowed_silently()
    {
        var logger = new RecordingLogger();
        var vm = new TestMonitorViewModel();

        await vm.RunAsync(() => throw new InvalidOperationException("worker unavailable"), logger);

        Assert.Contains(logger.Entries, e => e.Contains("could not load"));
    }

    private sealed class TestMonitorViewModel : MonitorViewModelBase
    {
        public Task RunAsync(Func<Task> load, ILogger? logger = null) =>
            RunLoadAsync(load, logger ?? NullLogger.Instance, "could not load");
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add(formatter(state, exception));
    }
}
