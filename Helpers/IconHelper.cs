using System.Drawing;

namespace ScreenShotManager.Helpers;

/// <summary>
/// Helper class for creating icons
/// </summary>
public static class IconHelper
{
    /// <summary>
    /// Creates the system tray icon
    /// </summary>
    public static Icon CreateTrayIcon()
    {
        var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.FillRectangle(new SolidBrush(Color.FromArgb(30, 144, 255)), 4, 4, 24, 24);
            g.DrawRectangle(new Pen(Color.White, 2), 6, 6, 20, 20);
            g.FillEllipse(Brushes.White, 12, 12, 8, 8);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }
}

