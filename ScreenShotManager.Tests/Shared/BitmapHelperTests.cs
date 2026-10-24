using System.Drawing;
using System.Windows.Media;
using ScreenShotManager.Shared.Helpers;
using Color = System.Drawing.Color;

namespace ScreenShotManager.Tests.Shared;

public class BitmapHelperTests
{
    private static Bitmap NewBitmap()
    {
        var bmp = new Bitmap(3, 2, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        bmp.SetPixel(0, 0, Color.FromArgb(255, 10, 20, 30));
        bmp.SetPixel(2, 1, Color.FromArgb(255, 200, 100, 50));
        return bmp;
    }

    private static byte[] Pixels(System.Windows.Media.Imaging.BitmapSource src)
    {
        var stride = src.PixelWidth * 4;
        var buffer = new byte[stride * src.PixelHeight];
        src.CopyPixels(buffer, stride, 0);
        return buffer;
    }

    [Fact]
    public void BitmapToBitmapSource_preserves_size_and_is_frozen()
    {
        using var bmp = NewBitmap();
        var src = BitmapHelper.BitmapToBitmapSource(bmp);

        Assert.Equal(3, src.PixelWidth);
        Assert.Equal(2, src.PixelHeight);
        Assert.True(src.IsFrozen);
    }

    [Fact]
    public void BitmapToBitmapSourceOptimized_copies_bgra_pixels()
    {
        using var bmp = NewBitmap();
        var src = BitmapHelper.BitmapToBitmapSourceOptimized(bmp);

        Assert.True(src.IsFrozen);
        Assert.Equal(PixelFormats.Bgra32, src.Format);
        var px = Pixels(src);
        Assert.Equal(new byte[] { 30, 20, 10, 255 }, px[0..4]);
        var last = (1 * 3 + 2) * 4;
        Assert.Equal(new byte[] { 50, 100, 200, 255 }, px[last..(last + 4)]);
    }

    [Fact]
    public void BitmapToBitmapSourceOptimized_leaves_bitmap_unlocked()
    {
        using var bmp = NewBitmap();
        BitmapHelper.BitmapToBitmapSourceOptimized(bmp);
        // LockBits would throw if the previous lock was not released.
        var data = bmp.LockBits(new Rectangle(0, 0, 3, 2),
            System.Drawing.Imaging.ImageLockMode.ReadOnly, bmp.PixelFormat);
        bmp.UnlockBits(data);
    }

    [Fact]
    public void BitmapSourceToBitmap_round_trips_pixels()
    {
        using var bmp = NewBitmap();
        var src = BitmapHelper.BitmapToBitmapSourceOptimized(bmp);
        using var back = BitmapHelper.BitmapSourceToBitmap(src);

        Assert.Equal(3, back.Width);
        Assert.Equal(2, back.Height);
        Assert.Equal(bmp.GetPixel(0, 0).ToArgb(), back.GetPixel(0, 0).ToArgb());
        Assert.Equal(bmp.GetPixel(2, 1).ToArgb(), back.GetPixel(2, 1).ToArgb());
    }
}
