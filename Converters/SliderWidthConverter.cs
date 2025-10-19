using System;
using System.Globalization;
using System.Windows.Data;

namespace ScreenShotManager.Converters;

public class SliderWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double sliderValue && parameter is double actualWidth)
        {
            return sliderValue * actualWidth / 100.0;
        }
        
        // For binding from template, we need multi-value converter
        return 0.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

