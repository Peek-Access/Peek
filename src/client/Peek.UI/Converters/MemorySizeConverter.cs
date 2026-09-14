using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Peek.UI.Converters;

/// <summary>Formats a byte count (Process Monitor's ProcessDto.MemoryBytes) as a human-readable string.</summary>
public class MemorySizeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long bytes || bytes <= 0) return "—";

        const double mb = 1024 * 1024;
        const double gb = mb * 1024;
        return bytes >= gb ? $"{bytes / gb:0.0} GB" : $"{bytes / mb:0.0} MB";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
