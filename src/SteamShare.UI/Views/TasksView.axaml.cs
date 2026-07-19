using Avalonia.Controls;
using Avalonia.Input;

using SteamShare.UI.ViewModels;

namespace SteamShare.UI.Views;

/// <summary>
/// View for the Tasks (任务) tab — displays active and completed tasks
/// with an expandable tree and download input bar.
/// </summary>
public partial class TasksView : UserControl
{
    public TasksView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Toggles the <see cref="TaskItemViewModel.IsExpanded"/> property
    /// when the task description area is tapped.
    /// </summary>
    private void OnTaskDescriptionTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control control
            && control.DataContext is TaskItemViewModel vm)
        {
            vm.IsExpanded = !vm.IsExpanded;
        }
    }
}
