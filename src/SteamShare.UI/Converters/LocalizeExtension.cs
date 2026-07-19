using System;

using Avalonia.Markup.Xaml;

using Microsoft.Extensions.DependencyInjection;

using SteamShare.Core.Localization;

namespace SteamShare.UI.Converters;

/// <summary>
/// Avalonia markup extension that resolves a localized string from
/// <see cref="LocalizationService"/> via the <see cref="AppServices.Provider"/>.
/// Usage: Content="{ext:Localize Key=Label_Downloads}"
/// </summary>
public sealed class LocalizeExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public LocalizeExtension()
    {
    }

    public LocalizeExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
        {
            return string.Empty;
        }

        var loc = AppServices.Provider.GetService<LocalizationService>();
        if (loc is null)
        {
            return Key;
        }

        return loc.GetString(Key);
    }
}
