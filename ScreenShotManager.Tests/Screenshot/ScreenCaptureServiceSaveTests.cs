using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using ScreenShotManager.Screenshot.Services;

namespace ScreenShotManager.Tests.Screenshot;

public class ScreenCaptureServiceSaveTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "ssm-save-" + Guid.NewGuid().ToString("N"));
    private readonly ScreenCaptureService service = new();

    public void Dispose()
    {
        service.Dispose();
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    private static Bitmap NewBitmap()
    {
        var bmp = new Bitmap(4, 4);
        bmp.SetPixel(1, 1, Color.Red);
        return bmp;
    }

    private static byte[] Header(string path, int count)
    {
        var bytes = File.ReadAllBytes(path);
        return bytes[..count];
    }

    [Theory]
    [InlineData("PNG", new byte[] { 0x89, 0x50, 0x4E, 0x47 })]
    [InlineData("png", new byte[] { 0x89, 0x50, 0x4E, 0x47 })]
    [InlineData("JPEG", new byte[] { 0xFF, 0xD8, 0xFF })]
    [InlineData("JPG", new byte[] { 0xFF, 0xD8, 0xFF })]
    [InlineData("BMP", new byte[] { 0x42, 0x4D })]
    [InlineData("WEBP", new byte[] { 0x89, 0x50, 0x4E, 0x47 })] // unknown -> PNG
    public async Task SaveToFileAsync_writes_requested_format(string format, byte[] magic)
    {
        var path = Path.Combine(dir, "sub", "shot.img");
        using var bmp = NewBitmap();

        await service.SaveToFileAsync(bmp, path, format);

        Assert.Equal(magic, Header(path, magic.Length));
    }

    [Fact]
    public async Task SaveToFileAsync_leaves_source_bitmap_usable()
    {
        var path = Path.Combine(dir, "shot.png");
        using var bmp = NewBitmap();

        await service.SaveToFileAsync(bmp, path, "PNG");

        Assert.Equal(Color.Red.ToArgb(), bmp.GetPixel(1, 1).ToArgb());
        using var reloaded = new Bitmap(path);
        Assert.Equal(Color.Red.ToArgb(), reloaded.GetPixel(1, 1).ToArgb());
    }

    [Fact]
    public async Task SaveToFileAsync_wraps_failure()
    {
        var path = Path.Combine(dir, "taken");
        Directory.CreateDirectory(path); // file path occupied by a directory

        using var bmp = NewBitmap();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveToFileAsync(bmp, path, "PNG"));
        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void ConvertToBitmapSource_and_back_preserves_size()
    {
        using var bmp = NewBitmap();
        var source = service.ConvertToBitmapSource(bmp);
        using var back = service.ConvertToBitmap(source);

        Assert.Equal(4, source.PixelWidth);
        Assert.Equal(4, back.Width);
        Assert.Equal(Color.Red.ToArgb(), back.GetPixel(1, 1).ToArgb());
    }
}
