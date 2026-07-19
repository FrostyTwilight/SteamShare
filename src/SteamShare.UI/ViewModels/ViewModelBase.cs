using CommunityToolkit.Mvvm.ComponentModel;

namespace SteamShare.UI.ViewModels;

public partial class ViewModelBase : ObservableObject, IDisposable
{
    public virtual void Dispose() { }
}
