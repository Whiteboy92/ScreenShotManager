using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using ScreenShotManager.Helpers;
using ScreenShotManager.Services.Interfaces;

namespace ScreenShotManager.Services;

/// <summary>
/// Service for screen capture operations with async support for better performance
/// </summary>
public class ScreenCaptureService : IScreenCaptureService
{
    public async Task<Bitmap> CaptureRegionAsync(int x, int y, int width, int height)
    {
        return await Task.Run(() =>
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
            
            graphics.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height), CopyPixelOperation.SourceCopy);
            return bitmap;
        });
    }

    public async Task SaveToFileAsync(Bitmap bitmap, string filePath, string format)
    {
        var bitmapCopy = new Bitmap(bitmap);
        
        await Task.Run(() =>
        {
            try
            {
                FileHelper.EnsureDirectoryExists(filePath);
                
                var imageFormat = format.ToUpper() switch
                {
                    "PNG" => ImageFormat.Png,
                    "JPEG" or "JPG" => ImageFormat.Jpeg,
                    "BMP" => ImageFormat.Bmp,
                    _ => ImageFormat.Png,
                };

                using (bitmapCopy)
                {
                    bitmapCopy.Save(filePath, imageFormat);
                }
            }
            catch (Exception ex)
            {
                bitmapCopy.Dispose();
                throw new InvalidOperationException($"Failed to save to {filePath}: {ex.Message}", ex);
            }
        });
    }

    public async Task CopyToClipboardAsync(Bitmap bitmap)
    {
        var bitmapCopy = new Bitmap(bitmap);
        
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var bitmapSource = ConvertToBitmapSource(bitmapCopy);
                Clipboard.SetImage(bitmapSource);
                bitmapCopy.Dispose();
            }
            catch (Exception ex)
            {
                bitmapCopy.Dispose();
                throw new InvalidOperationException($"Failed to copy to clipboard: {ex.Message}", ex);
            }
        });
    }

    public BitmapSource ConvertToBitmapSource(Bitmap bitmap)
    {
        return BitmapHelper.BitmapToBitmapSource(bitmap);
    }

    public Bitmap ConvertToBitmap(BitmapSource bitmapSource)
    {
        return BitmapHelper.BitmapSourceToBitmap(bitmapSource);
    }
}

