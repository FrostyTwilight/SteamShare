using Avalonia;
using Avalonia.Styling;

using Serilog;

namespace SteamShare.UI.Services;

/// <summary>
/// Manages application theme switching by updating
/// <see cref="Application.Current.RequestedThemeVariant"/>.
/// </summary>
public sealed class ThemeService
{
    private static readonly ILogger LogSerilog = Log.ForContext<ThemeService>();

    /// <summary>
    /// Applies the specified theme. Accepted values: "System", "Light", "Dark".
    /// </summary>
    /// <param name="theme">Theme name ("System", "Light", or "Dark").</param>
    public void ApplyTheme(string theme)
    {
        if (Application.Current is null)
        {
            LogSerilog.Warning("Cannot apply theme: Application.Current is null");
            return;
        }

        LogSerilog.Information("Applying theme: {Theme}", theme);

        Application.Current.RequestedThemeVariant = theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
