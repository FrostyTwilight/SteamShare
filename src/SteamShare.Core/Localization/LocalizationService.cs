using System.Globalization;
using System.Resources;

namespace SteamShare.Core.Localization;

/// <summary>
/// Provides localized strings using .NET RESX resources.
/// Falls back to en-US when a key is missing in the current culture.
/// </summary>
public sealed class LocalizationService
{
    private readonly ResourceManager _resourceManager;
    private CultureInfo _currentCulture;

    public LocalizationService()
    {
        _resourceManager = new ResourceManager("SteamShare.Core.Localization.Strings", typeof(LocalizationService).Assembly);
        _currentCulture = CultureInfo.CurrentUICulture;
    }

    /// <summary>
    /// Get a localized string by its resource key.
    /// </summary>
    public string GetString(string key)
    {
        var value = _resourceManager.GetString(key, _currentCulture);
        if (value != null)
        {
            return value;
        }

        // Fallback to en-US
        value = _resourceManager.GetString(key, new CultureInfo("en-US"));
        return value ?? key;
    }

    /// <summary>
    /// Get a formatted localized string.
    /// </summary>
    public string GetString(string key, params object[] args)
    {
        var template = GetString(key);
        return string.Format(_currentCulture, template, args);
    }

    /// <summary>
    /// Change the current UI culture and refresh all localized strings.
    /// </summary>
    public void SetCulture(string cultureName)
    {
        _currentCulture = new CultureInfo(cultureName);
        CultureInfo.CurrentUICulture = _currentCulture;
    }

    /// <summary>
    /// The current UI culture name (e.g., "zh-CN", "en-US").
    /// </summary>
    public string CurrentCultureName => _currentCulture.Name;
}
