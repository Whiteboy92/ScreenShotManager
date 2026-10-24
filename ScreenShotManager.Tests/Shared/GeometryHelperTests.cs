using System.Windows;
using ScreenShotManager.Shared.Helpers;

namespace ScreenShotManager.Tests.Shared;

public class GeometryHelperTests
{
    [Fact]
    public void CreateRectFromPoints_normalizes_start_after_end()
    {
        var rect = GeometryHelper.CreateRectFromPoints(new Point(50, 60), new Point(10, 20));
        Assert.Equal(new Rect(10, 20, 40, 40), rect);
    }

    [Fact]
    public void CreateRectFromPoints_zero_area_when_points_equal()
    {
        var rect = GeometryHelper.CreateRectFromPoints(new Point(5, 5), new Point(5, 5));
        Assert.Equal(0, rect.Width);
        Assert.Equal(0, rect.Height);
    }

    [Theory]
    [InlineData(10, 10, true)]
    [InlineData(9, 100, false)]
    [InlineData(100, 9, false)]
    [InlineData(0, 0, false)]
    public void IsValidSelection_applies_min_size(double w, double h, bool expected)
    {
        Assert.Equal(expected, GeometryHelper.IsValidSelection(new Rect(0, 0, w, h)));
    }

    [Fact]
    public void IsValidSelection_respects_custom_min()
    {
        Assert.True(GeometryHelper.IsValidSelection(new Rect(0, 0, 5, 5), minSize: 5));
        Assert.False(GeometryHelper.IsValidSelection(new Rect(0, 0, 4, 4), minSize: 5));
    }

    [Fact]
    public void GetVirtualScreenBounds_matches_system_parameters()
    {
        var bounds = GeometryHelper.GetVirtualScreenBounds();
        Assert.Equal(SystemParameters.VirtualScreenLeft, bounds.X);
        Assert.Equal(SystemParameters.VirtualScreenTop, bounds.Y);
        Assert.Equal(SystemParameters.VirtualScreenWidth, bounds.Width);
        Assert.Equal(SystemParameters.VirtualScreenHeight, bounds.Height);
    }
}
