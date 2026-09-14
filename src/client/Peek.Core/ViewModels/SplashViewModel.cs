using AsyncNavigation.Abstractions;
using AsyncNavigation.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Peek.Core.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Peek.Core.ViewModels;
public partial class SplashViewModel : ViewModelBase, IDialogAware
{
    [Reactive]
    private int _ratio;

    private CancellationTokenSource? _cts;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private Task? _loadingTask;

    public SplashViewModel(IServiceProvider serviceProvider, ILogger<SplashViewModel> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public event AsyncEventHandler<DialogCloseEventArgs>? RequestCloseAsync;
    public string Title => string.Empty;

    [ReactiveCommand]
    private Task CloseDialog(string param)
    {
        if (RequestCloseAsync is null) return Task.CompletedTask;

        var result = Ratio == 100 ? DialogButtonResult.Done : DialogButtonResult.Cancel;
        return RequestCloseAsync.Invoke(this,
            new DialogCloseEventArgs(new DialogResult(result), CancellationToken.None));
    }

    public Task OnDialogOpenedAsync(IDialogParameters? parameters, CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _loadingTask = _serviceProvider.GetRequiredService<AudioPlayer>().VlcInitializeAsync();
        _ = StartProgressAsync(Task.WhenAll(_loadingTask, Task.Delay(2000)), _cts.Token);
        return Task.CompletedTask;
    }

    public Task OnDialogClosingAsync(IDialogResult? dialogResult, CancellationToken cancellationToken)
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (Ratio != 100) cts?.Cancel();
        cts?.Dispose();
        return Task.CompletedTask;
    }

    public Task OnDialogClosedAsync(IDialogResult? dialogResult, CancellationToken cancellationToken)
        => Task.CompletedTask;

    private async Task StartProgressAsync(Task loadingTask, CancellationToken token)
    {
        Ratio = 0;

        try
        {
            var start = DateTimeOffset.UtcNow;

            var progressTask = Task.Run(async () =>
            {
                const double k = 1.2;

                while (!token.IsCancellationRequested && !loadingTask.IsCompleted)
                {
                    var t = (DateTimeOffset.UtcNow - start).TotalSeconds;

                    var value = 1.0 - Math.Exp(-k * t);

                    var next = Math.Min(99, (int)(value * 100));

                    Ratio = next;

                    await Task.Delay(30, token);
                }
            }, token);

            Ratio = 100;

            try { await progressTask; } catch { }

            if (token.IsCancellationRequested) return;

            if (RequestCloseAsync is not null)
            {
                await RequestCloseAsync.Invoke(this,
                    new DialogCloseEventArgs(
                        new DialogResult(DialogButtonResult.Done),
                        CancellationToken.None));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Splash failed");

            if (RequestCloseAsync is not null)
            {
                await RequestCloseAsync.Invoke(this,
                    new DialogCloseEventArgs(
                        new DialogResult(DialogButtonResult.Cancel),
                        CancellationToken.None));
            }
        }
    }
}