using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ScreenShotManager.Shared.Helpers;

/// <summary>
/// Helper class for bitmap conversions
/// </summary>
public static class BitmapHelper
{
    public static BitmapSource BitmapToBitmapSource(Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions()
            );
            
            bitmapSource.Freeze();
            return bitmapSource;
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    public static BitmapSource BitmapToBitmapSourceOptimized(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var format = System.Windows.Media.PixelFormats.Bgra32;
        var stride = width * 4;
        
        var data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        
        try
        {
            var bitmapSource = BitmapSource.Create(
                width, height,
                96, 96, // DPI
                format,
                null,
                data.Scan0,
                stride * height,
                stride);
            
            bitmapSource.Freeze();
            return bitmapSource;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
    
    public static Bitmap BitmapSourceToBitmap(BitmapSource bitmapSource)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmapSource));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}

