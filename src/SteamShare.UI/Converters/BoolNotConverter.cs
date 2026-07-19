using System;
using System.Globalization;

using Avalonia.Data.Converters;

namespace SteamShare.UI.Converters;

/// <summary>
/// Inverts a boolean value. Returns <c>false</c> when input is <c>true</c> and vice versa.
/// </summary>
public sealed class BoolNotConverter : IValueConverter
{
    public static readonly BoolNotConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }

        return false;
    }
}
