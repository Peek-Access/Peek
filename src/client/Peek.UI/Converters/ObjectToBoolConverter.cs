using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Peek.UI.Converters;

public class ObjectToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
            return false;
        if (parameter is null)
            return false;
        return value.Equals(parameter);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
