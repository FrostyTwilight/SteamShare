using System.Collections.ObjectModel;
using System.Reflection;

using CommunityToolkit.Mvvm.ComponentModel;

using Serilog;

using SteamShare.Core.Localization;

namespace SteamShare.UI.ViewModels;

/// <summary>
/// Represents a dependency displayed in the About section.
/// </summary>
public class DependencyInfo
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}

/// <summary>
/// ViewModel for the About section.
/// Displays app info, license, Steam disclaimer, and loaded dependencies.
/// </summary>
public partial class AboutViewModel : ViewModelBase
{
    private static readonly ILogger LogSerilog = Log.ForContext<AboutViewModel>();
    private readonly LocalizationService _loc;

    /// <summary>Application name.</summary>
    public string AppName => _loc.GetString("App_Name");

    /// <summary>Application version from the entry assembly.</summary>
    public string Version { get; }

    /// <summary>License identifier.</summary>
    public string License => "CC BY-NC-SA 4.0";

    /// <summary>Source code repository URL.</summary>
    public string RepoUrl => "https://github.com/FrostyTwilight/SteamShare";

    /// <summary>Steam trademark disclaimer text.</summary>
    public string SteamDisclaimer => _loc.GetString("UI_About_SteamDisclaimerBody");

    /// <summary>Loaded dependencies with their versions.</summary>
    public ObservableCollection<DependencyInfo> Dependencies { get; } = new();

    public AboutViewModel(LocalizationService loc)
    {
        _loc = loc;
        Version = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? "1.0.0";

        LogSerilog.Debug("AboutViewModel created: version={Version}", Version);
        LoadDependencies();
    }

    private void LoadDependencies()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .OrderBy(a => a.GetName().Name);

        foreach (var asm in assemblies)
        {
            var name = asm.GetName();
            if (name.Name is null)
            {
                continue;
            }

            // Skip system and internal assemblies
            if (name.Name.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
                || name.Name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
                || name.Name.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)
                || name.Name.StartsWith("WindowsBase", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Dependencies.Add(new DependencyInfo
            {
                Name = name.Name,
                Version = name.Version?.ToString() ?? _loc.GetString("Label_UnknownVersion"),
            });
        }
    }
}
