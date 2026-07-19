using System;
using System.Globalization;

using Avalonia.Data.Converters;

using SteamShare.Core.Utilities;

namespace SteamShare.UI.Converters;

/// <summary>
/// Converts a <see cref="long"/> byte count to a human-readable size string
/// via <see cref="FileSizeFormatter.Format"/>.
/// </summary>
public class FileSizeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long bytes)
        {
            return FileSizeFormatter.Format(bytes);
        }
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
