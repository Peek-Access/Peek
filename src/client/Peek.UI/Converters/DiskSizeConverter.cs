using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Peek.UI.Converters;

/// <summary>Formats a nullable KB size (App Monitor's InstalledAppDto.EstimatedSizeKb) as a human-readable string, or a placeholder when the OS never recorded one.</summary>
public class DiskSizeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long kb || kb <= 0) return "—";

        return kb switch
        {
            >= 1024 * 1024 => $"{kb / (1024d * 1024):0.0} GB",
            >= 1024 => $"{kb / 1024d:0.0} MB",
            _ => $"{kb} KB",
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
