using System;
using System.Drawing;
using System.IO;

namespace ScreenShotManager.Shared.Helpers;

/// <summary>
/// Helper class for loading application icons
/// </summary>
public static class IconHelper
{
    /// <summary>
    /// Creates the system tray icon
    /// </summary>
    public static Icon CreateTrayIcon()
    {
        try
        {
            // Try to load icon from file
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Icon", "camera_2925432.ico");
            
            if (File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }
            
            // Fallback: create simple icon programmatically
            return CreateFallbackIcon();
        }
        catch
        {
            return CreateFallbackIcon();
        }
    }
    
    private static Icon CreateFallbackIcon()
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

