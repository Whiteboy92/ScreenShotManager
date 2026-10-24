using System;
using System.Runtime.InteropServices;
using ScreenShotManager.Screenshot.Services;

namespace ScreenShotManager.Tests.Screenshot;

public class HdrDecodeTests
{
    [Theory]
    [InlineData(0f, 0)]
    [InlineData(-1f, 0)]
    [InlineData(float.NaN, 0)]
    [InlineData(1f, 255)]
    [InlineData(5f, 255)]
    [InlineData(float.PositiveInfinity, 255)]
    [InlineData(0.5f, 188)]
    [InlineData(0.001f, 3)]
    [InlineData(0.2140f, 127)]
    public void LinearToSrgb_matches_reference_values(float linear, byte expected)
    {
        Assert.Equal(expected, HdrScreenCapturer.LinearToSrgb(linear));
    }

    [Fact]
    public void LinearToSrgb_is_monotonic()
    {
        byte prev = 0;
        for (int i = 0; i <= 1000; i++)
        {
            var v = HdrScreenCapturer.LinearToSrgb(i / 1000f);
            Assert.True(v >= prev, $"not monotonic at {i}");
            prev = v;
        }
    }

    [Fact]
    public void DecodeBgra8_strips_row_padding()
    {
        const int width = 2, height = 3, rowPitch = 16; // 8 bytes of pixels + 8 bytes padding
        var src = new byte[rowPitch * height];
        for (int y = 0; y < height; y++)
        {
            for (int b = 0; b < rowPitch; b++)
            {
                src[y * rowPitch + b] = b < width * 4 ? (byte)(y * 10 + b) : (byte)0xEE;
            }
        }

        var dst = new byte[width * 4 * height];
        var handle = GCHandle.Alloc(src, GCHandleType.Pinned);
        try
        {
            HdrScreenCapturer.DecodeBgra8(handle.AddrOfPinnedObject(), rowPitch, width, height, dst);
        }
        finally
        {
            handle.Free();
        }

        for (int y = 0; y < height; y++)
        {
            for (int b = 0; b < width * 4; b++)
            {
                Assert.Equal((byte)(y * 10 + b), dst[y * width * 4 + b]);
            }
        }
    }

    private static (float max, float white) Analyze(float r, float g, float b, int width = 8, int height = 8)
    {
        var pixels = new ushort[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            pixels[i * 4] = BitConverter.HalfToUInt16Bits((Half)r);
            pixels[i * 4 + 1] = BitConverter.HalfToUInt16Bits((Half)g);
            pixels[i * 4 + 2] = BitConverter.HalfToUInt16Bits((Half)b);
            pixels[i * 4 + 3] = BitConverter.HalfToUInt16Bits((Half)1f);
        }

        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            return HdrScreenCapturer.AnalyzeFp16(handle.AddrOfPinnedObject().ToInt64(), width * 8, width, height);
        }
        finally
        {
            handle.Free();
        }
    }

    [Fact]
    public void AnalyzeFp16_black_frame_defaults_white_to_one()
    {
        var (max, white) = Analyze(0f, 0f, 0f);
        Assert.Equal(0f, max);
        Assert.Equal(1f, white);
    }

    [Fact]
    public void AnalyzeFp16_sdr_frame_has_max_at_most_one()
    {
        var (max, white) = Analyze(0.5f, 0.25f, 1f);
        Assert.Equal(1f, max);
        Assert.Equal(1f, white); // clamped: SDR never lowers white point below 1
    }

    [Fact]
    public void AnalyzeFp16_hdr_frame_reports_peak_and_raised_white_point()
    {
        var (max, white) = Analyze(8f, 8f, 8f);
        Assert.Equal(8f, max);
        // Luminance 8 -> log2 = 3; histogram bin center should land near it.
        Assert.InRange(white, 7f, 9f);
    }
}
