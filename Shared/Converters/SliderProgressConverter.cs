using System;
using System.Globalization;
using System.Windows.Data;

namespace ScreenShotManager.Shared.Converters;

public class SliderProgressConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is [double currentValue, double maximum, double actualWidth, ..])
        {
            if (maximum > 0)
            {
                return (currentValue / maximum) * actualWidth;
            }
        }
        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

