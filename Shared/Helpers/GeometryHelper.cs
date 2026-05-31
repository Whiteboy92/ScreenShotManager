using System.Windows;

namespace ScreenShotManager.Shared.Helpers;

/// <summary>
/// Helper class for geometry calculations
/// </summary>
public static class GeometryHelper
{
    /// <summary>
    /// Creates a rectangle from two points
    /// </summary>
    public static Rect CreateRectFromPoints(Point start, Point end)
    {
        var x = System.Math.Min(start.X, end.X);
        var y = System.Math.Min(start.Y, end.Y);
        var width = System.Math.Abs(end.X - start.X);
        var height = System.Math.Abs(end.Y - start.Y);

        return new Rect(x, y, width, height);
    }

    /// <summary>
    /// Checks if a rectangle is valid (has minimum dimensions)
    /// </summary>
    public static bool IsValidSelection(Rect rect, double minSize = 10)
    {
        return rect.Width >= minSize && rect.Height >= minSize;
    }

    /// <summary>
    /// Gets the virtual screen bounds for multi-monitor support
    /// </summary>
    public static Rect GetVirtualScreenBounds()
    {
        return new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight
        );
    }
}

