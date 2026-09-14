using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Peek.Core.Models;
using System;
using System.Globalization;

namespace Peek.UI.Converters
{
    public class ColorModelToBrushConverter : IValueConverter
    {
        public static readonly ColorModelToBrushConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ColorModel c)
            {
                return new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
            }

            return AvaloniaProperty.UnsetValue;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if(value is Brush brush)
            {
                return ColorModel.Parse(brush.ToString());
            }
            return default(ColorModel);
        }
    }
}
