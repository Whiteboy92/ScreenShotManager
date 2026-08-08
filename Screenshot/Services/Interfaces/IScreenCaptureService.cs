using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace ScreenShotManager.Screenshot.Services.Interfaces;

/// <summary>
/// Interface for screen capture operations
/// </summary>
public interface IScreenCaptureService : IDisposable
{
    Bitmap CaptureRegion(int x, int y, int width, int height);
    Task<Bitmap> CaptureRegionAsync(int x, int y, int width, int height);
    Task SaveToFileAsync(Bitmap bitmap, string filePath, string format);
    Task CopyToClipboardAsync(Bitmap bitmap);
    BitmapSource ConvertToBitmapSource(Bitmap bitmap);
    Bitmap ConvertToBitmap(BitmapSource bitmapSource);

    /// <summary>
    /// Warms the DXGI capture pipeline (device creation, duplication, first frame, JIT) on a
    /// background thread so the first Print Screen press isn't cold. Best-effort; never throws.
    /// </summary>
    void WarmUp();
}

