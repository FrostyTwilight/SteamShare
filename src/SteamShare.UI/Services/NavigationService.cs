using Microsoft.Extensions.DependencyInjection;

using Serilog;

using SteamShare.UI.ViewModels;

namespace SteamShare.UI.Services;

/// <summary>
/// ViewModel-first navigation service.
/// Resolves ViewModels from DI and sets them as the shell's current view.
/// Avalonia's DataTemplate system maps the ViewModel type to the correct View automatically.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private static readonly ILogger LogSerilog = Log.ForContext<NavigationService>();
    private readonly IServiceProvider _serviceProvider;

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc/>
    public void NavigateTo<TViewModel>()
        where TViewModel : ViewModelBase
    {
        LogSerilog.Debug("Navigating to {ViewModel}", typeof(TViewModel).Name);
        var viewModel = _serviceProvider.GetRequiredService<TViewModel>();
        var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();
        if (mainVm.CurrentView is IDisposable disposable)
        {
            disposable.Dispose();
        }
        mainVm.CurrentView = viewModel;
        LogSerilog.Information("Navigated to {ViewModel}", typeof(TViewModel).Name);
    }
}
