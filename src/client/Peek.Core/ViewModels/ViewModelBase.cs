using AsyncNavigation;
using AsyncNavigation.Abstractions;
using AsyncNavigation.Core;
using ReactiveUI;

namespace Peek.Core.ViewModels;

public partial class ViewModelBase : ReactiveObject, INavigationAware
{
    public event AsyncEventHandler<AsyncEventArgs>? AsyncRequestUnloadEvent;

    public virtual Task InitializeAsync(NavigationContext context)
    {
        return Task.CompletedTask;
    }

    public virtual Task<bool> IsNavigationTargetAsync(NavigationContext context)
    {
        return Task.FromResult(true);
    }

    public virtual Task OnNavigatedFromAsync(NavigationContext context)
    {
        return Task.CompletedTask;
    }

    public virtual Task OnNavigatedToAsync(NavigationContext context)
    {
        return Task.CompletedTask;
    }

    public virtual Task OnUnloadAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
