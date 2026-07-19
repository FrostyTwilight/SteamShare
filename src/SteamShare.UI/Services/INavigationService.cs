using SteamShare.UI.ViewModels;

namespace SteamShare.UI.Services;

/// <summary>
/// ViewModel-first navigation using ContentControl DataTemplate resolution.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Navigates to the view associated with <typeparamref name="TViewModel"/>.
    /// Resolves the ViewModel from DI, sets it as the shell's current view,
    /// and Avalonia's DataTemplate system resolves the matching View.
    /// </summary>
    void NavigateTo<TViewModel>()
        where TViewModel : ViewModelBase;
}
