using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using ScreenShotManager.Shared.Helpers;
using ScreenShotManager.Screenshot.Services.Interfaces;

namespace ScreenShotManager.Screenshot.Services;

/// <summary>
/// Service for screen capture operations with async support for better performance
/// </summary>
public class ScreenCaptureService : IScreenCaptureService
{
    private readonly HdrScreenCapturer hdrCapturer = new();

    public Bitmap CaptureRegion(int x, int y, int width, int height)
    {
        Bitmap? full = null;
        try
        {
            CaptureLog.Session($"CaptureRegion x={x} y={y} w={width} h={height}");
            full = hdrCapturer.CaptureVirtualDesktop(out int originX, out int originY);
            CaptureLog.Write($"DXGI path OK. full={full.Width}x{full.Height} origin=({originX},{originY})");

            int cropX = x - originX;
            int cropY = y - originY;

            // Whole-desktop request (the overlay's normal case): hand back as-is.
            if (cropX <= 0 && cropY <= 0 && width >= full.Width && height >= full.Height)
            {
                return full;
            }

            cropX = Math.Max(0, Math.Min(cropX, full.Width - 1));
            cropY = Math.Max(0, Math.Min(cropY, full.Height - 1));
            int cropW = Math.Min(width, full.Width - cropX);
            int cropH = Math.Min(height, full.Height - cropY);

            using (full)
            {
                return full.Clone(new Rectangle(cropX, cropY, cropW, cropH), full.PixelFormat);
            }
        }
        catch (Exception ex)
        {
            full?.Dispose();
            CaptureLog.Write($"DXGI FAILED -> GDI fallback (HDR will clip bright). {ex.GetType().Name}: {ex.Message}");
            return CaptureRegionGdi(x, y, width, height);
        }
    }

    /// <summary>
    /// Legacy GDI capture. Fallback only — does not handle HDR (clips bright content to white).
    /// </summary>
    private static Bitmap CaptureRegionGdi(int x, int y, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;

        graphics.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height), CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    public async Task<Bitmap> CaptureRegionAsync(int x, int y, int width, int height)
    {
        return await Task.Run(() => CaptureRegion(x, y, width, height));
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

