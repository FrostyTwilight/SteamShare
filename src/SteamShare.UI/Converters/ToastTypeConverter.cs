using System;
using System.Globalization;

using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SteamShare.UI.Converters;

/// <summary>
/// Converts a <see cref="Services.ToastType"/> to a background <see cref="IBrush"/>.
/// Error → red, Warning → amber, Info → blue.
/// </summary>
public sealed class ToastTypeConverter : IValueConverter
{
    public static readonly ToastTypeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Services.ToastType type)
        {
            return type switch
            {
                Services.ToastType.Error => new SolidColorBrush(Color.Parse("#D32F2F")),
                Services.ToastType.Warning => new SolidColorBrush(Color.Parse("#F9A825")),
                Services.ToastType.Info => new SolidColorBrush(Color.Parse("#1976D2")),
                _ => new SolidColorBrush(Color.Parse("#757575")),
            };
        }

        return new SolidColorBrush(Color.Parse("#757575"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
