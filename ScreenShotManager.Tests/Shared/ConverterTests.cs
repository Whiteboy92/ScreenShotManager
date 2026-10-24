using System;
using System.Globalization;
using ScreenShotManager.Shared.Converters;

namespace ScreenShotManager.Tests.Shared;

public class ConverterTests
{
    [Fact]
    public void SliderProgress_scales_value_to_width()
    {
        var result = new SliderProgressConverter().Convert(
            new object[] { 25.0, 100.0, 200.0 }, typeof(double), null!, CultureInfo.InvariantCulture);
        Assert.Equal(50.0, result);
    }

    [Fact]
    public void SliderProgress_zero_when_maximum_not_positive()
    {
        var result = new SliderProgressConverter().Convert(
            new object[] { 25.0, 0.0, 200.0 }, typeof(double), null!, CultureInfo.InvariantCulture);
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void SliderProgress_zero_on_unexpected_inputs()
    {
        var conv = new SliderProgressConverter();
        Assert.Equal(0.0, conv.Convert(new object[] { 1.0, 2.0 }, typeof(double), null!, CultureInfo.InvariantCulture));
        Assert.Equal(0.0, conv.Convert(new object[] { "1", 2.0, 3.0 }, typeof(double), null!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SliderProgress_ConvertBack_not_supported()
    {
        Assert.Throws<NotImplementedException>(() =>
            new SliderProgressConverter().ConvertBack(0.0, new[] { typeof(double) }, null!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SliderWidth_scales_percentage_by_parameter()
    {
        var result = new SliderWidthConverter().Convert(50.0, typeof(double), 300.0, CultureInfo.InvariantCulture);
        Assert.Equal(150.0, result);
    }

    [Fact]
    public void SliderWidth_zero_on_unexpected_inputs()
    {
        var conv = new SliderWidthConverter();
        Assert.Equal(0.0, conv.Convert(50.0, typeof(double), "300", CultureInfo.InvariantCulture));
        Assert.Equal(0.0, conv.Convert(null, typeof(double), 300.0, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SliderWidth_ConvertBack_not_supported()
    {
        Assert.Throws<NotImplementedException>(() =>
            new SliderWidthConverter().ConvertBack(0.0, typeof(double), null, CultureInfo.InvariantCulture));
    }
}
