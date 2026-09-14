using Avalonia.Data.Converters;
using System;
using System.Collections;
using System.Globalization;
using System.Linq;

namespace Peek.UI.Converters;

/// <summary>
/// For the Element Inspector's "show every property" detail panel: null, an empty/blank
/// string, or an empty collection all render as the placeholder (ConverterParameter, or
/// "—" by default) instead of blank space, so a field that's genuinely empty reads as
/// "confirmed empty" rather than looking like a rendering glitch. Everything else falls
/// back to ToString() - covers bools, enums, nullable enums/bools, ints, etc. uniformly.
/// </summary>
public class PlaceholderConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var placeholder = parameter as string ?? "—";

        switch (value)
        {
            case null:
                return placeholder;
            case string s:
                return string.IsNullOrWhiteSpace(s) ? placeholder : s;
            case IEnumerable enumerable and not string:
                var items = enumerable.Cast<object?>().Select(o => o?.ToString()).ToList();
                return items.Count == 0 ? placeholder : string.Join(", ", items);
            default:
                return value.ToString();
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
