using Microsoft.Extensions.DependencyInjection;

namespace SteamShare.CLI;

/// <summary>
/// Static service locator for CLI commands to resolve core services.
/// Set by Program.cs after the host is built.
/// </summary>
public static class AppServices
{
    /// <summary>
    /// The application's <see cref="IServiceProvider"/>. Set once during startup.
    /// </summary>
    public static IServiceProvider Provider { get; set; } = null!;

    /// <summary>
    /// Resolves a required service of type <typeparamref name="T"/>.
    /// </summary>
    public static T Get<T>() where T : notnull
    {
        return Provider.GetRequiredService<T>();
    }
}
