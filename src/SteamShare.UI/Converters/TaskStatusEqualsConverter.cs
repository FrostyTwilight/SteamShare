using System;
using System.Globalization;

using Avalonia.Data.Converters;

using SteamShare.Core.Tasks;

using TaskStatus = SteamShare.Core.Tasks.TaskStatus;

namespace SteamShare.UI.Converters;

/// <summary>
/// Compares a <see cref="TaskStatus"/> value against a string parameter
/// (e.g., "Running", "Completed", "Failed") and returns <c>true</c> on match.
/// </summary>
public sealed class TaskStatusEqualsConverter : IValueConverter
{
    public static readonly TaskStatusEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TaskStatus status
            && parameter is string targetStatus
            && Enum.TryParse<TaskStatus>(targetStatus, ignoreCase: true, out var target))
        {
            return status == target;
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("TaskStatusEqualsConverter does not support ConvertBack.");
}
