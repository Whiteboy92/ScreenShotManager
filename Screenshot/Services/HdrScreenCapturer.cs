using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using ScreenShotManager.Shared.Helpers;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace ScreenShotManager.Screenshot.Services;

/// <summary>
/// Captures the whole virtual desktop via the DXGI Desktop Duplication API.
///
/// Unlike GDI <c>CopyFromScreen</c> (which reads an 8-bit buffer and clips bright
/// HDR content to white), this reads the composed surface as 16-bit float scRGB and
/// tone-maps HDR outputs down to SDR — the same idea the Windows Snipping Tool uses,
/// so bright HDR areas no longer come out "ultra white".
///
/// Assumptions / limitations:
/// - Coordinates are physical pixels; the pipeline assumes 100% / system DPI scaling.
/// - Display rotation is not handled (landscape only).
/// - HDR→SDR is an approximation (paper-white normalization + ACES filmic curve).
/// </summary>
internal sealed class HdrScreenCapturer
{
    private sealed record RegionPixels(int Left, int Top, int Width, int Height, byte[] Bgra);

    /// <summary>
    /// Captures every attached output and stitches them into one bitmap.
    /// <paramref name="originX"/>/<paramref name="originY"/> give the top-left of the
    /// virtual desktop in physical pixels (matches WPF virtual-screen origin at 100% DPI).
    /// </summary>
    public Bitmap CaptureVirtualDesktop(out int originX, out int originY)
    {
        var regions = new List<RegionPixels>();

        using (var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>())
        {
            for (int ai = 0; factory.EnumAdapters1((uint)ai, out IDXGIAdapter1 adapter).Success; ai++)
            {
                using (adapter)
                {
                    CaptureAdapterOutputs(adapter, regions);
                }
            }
        }

        if (regions.Count == 0)
        {
            throw new InvalidOperationException("DXGI Desktop Duplication returned no outputs.");
        }

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var r in regions)
        {
            minX = Math.Min(minX, r.Left);
            minY = Math.Min(minY, r.Top);
            maxX = Math.Max(maxX, r.Left + r.Width);
            maxY = Math.Max(maxY, r.Top + r.Height);
        }

        originX = minX;
        originY = minY;
        int width = maxX - minX;
        int height = maxY - minY;

        return Composite(regions, minX, minY, width, height);
    }

    private void CaptureAdapterOutputs(IDXGIAdapter1 adapter, List<RegionPixels> regions)
    {
        ID3D11Device? device = null;
        ID3D11DeviceContext? context = null;
        try
        {
            D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                null, out device).CheckError();
            context = device!.ImmediateContext;

            for (int oi = 0; adapter.EnumOutputs((uint)oi, out IDXGIOutput output).Success; oi++)
            {
                using (output)
                {
                    IDXGIOutput6? output6 = null;
                    try
                    {
                        output6 = output.QueryInterface<IDXGIOutput6>();
                    }
                    catch
                    {
                        continue; // pre-Win10-1703 output; skip
                    }

                    using (output6)
                    {
                        var desc = output6.Description;
                        if (!desc.AttachedToDesktop)
                        {
                            continue;
                        }

                        var region = CaptureOutput(device, context, output6, desc);
                        if (region != null)
                        {
                            regions.Add(region);
                        }
                    }
                }
            }
        }
        finally
        {
            context?.Dispose();
            device?.Dispose();
        }
    }

    private RegionPixels? CaptureOutput(ID3D11Device device, ID3D11DeviceContext context,
        IDXGIOutput6 output6, OutputDescription desc)
    {
        var coords = desc.DesktopCoordinates;
        var colorSpace = output6.Description1.ColorSpace;
        CaptureLog.Write($"Output {desc.DeviceName} rect=({coords.Left},{coords.Top},{coords.Right},{coords.Bottom}) colorSpace={colorSpace}");

        IDXGIOutputDuplication? duplication;
        try
        {
            duplication = output6.DuplicateOutput1(device, 0, new[] { Format.R16G16B16A16_Float });
        }
        catch (Exception ex)
        {
            CaptureLog.Write($"  DuplicateOutput1 FAILED: {ex.GetType().Name}: {ex.Message}");
            return null; // protected content / unsupported; leave region blank
        }

        using (duplication)
        {
            IDXGIResource? frameResource = null;
            for (int attempt = 0; attempt < 5 && frameResource == null; attempt++)
            {
                Result result = duplication.AcquireNextFrame(500, out _, out frameResource);
                if (result.Failure)
                {
                    frameResource = null;
                    if (result == Vortice.DXGI.ResultCode.WaitTimeout)
                    {
                        continue;
                    }
                    CaptureLog.Write($"  AcquireNextFrame FAILED: {result}");
                    return null;
                }
            }

            if (frameResource == null)
            {
                return null;
            }

            try
            {
                using var texture = frameResource.QueryInterface<ID3D11Texture2D>();
                var td = texture.Description;
                CaptureLog.Write($"  Frame acquired. textureFormat={td.Format} {td.Width}x{td.Height}");

                var stagingDesc = new Texture2DDescription
                {
                    Width = td.Width,
                    Height = td.Height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = td.Format,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CPUAccessFlags = CpuAccessFlags.Read,
                    MiscFlags = ResourceOptionFlags.None,
                };

                using var staging = device.CreateTexture2D(stagingDesc);
                context.CopyResource(staging, texture);

                MappedSubresource map = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    byte[] bgra = ConvertFp16ToBgra(map.DataPointer, (int)map.RowPitch, (int)td.Width, (int)td.Height);
                    return new RegionPixels(coords.Left, coords.Top, (int)td.Width, (int)td.Height, bgra);
                }
                finally
                {
                    context.Unmap(staging, 0);
                }
            }
            finally
            {
                frameResource.Dispose();
                duplication.ReleaseFrame();
            }
        }
    }

    private static Bitmap Composite(List<RegionPixels> regions, int originX, int originY, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            foreach (var r in regions)
            {
                int dx = r.Left - originX;
                int dy = r.Top - originY;
                int rowBytes = r.Width * 4;

                for (int y = 0; y < r.Height; y++)
                {
                    IntPtr dest = IntPtr.Add(data.Scan0, (dy + y) * data.Stride + dx * 4);
                    Marshal.Copy(r.Bgra, y * rowBytes, dest, rowBytes);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    private static byte[] ConvertFp16ToBgra(IntPtr src, int rowPitch, int width, int height)
    {
        var outBuf = new byte[width * height * 4];
        long srcAddr = src.ToInt64();

        // Detect HDR from pixels, not the color-space enum: desktop duplication in
        // R16G16B16A16_Float delivers scRGB (RgbFullG10NoneP709, linear, 1.0 == 80 nits),
        // NOT PQ/G2084 — so any channel above 1.0 means real HDR highlights. SDR content
        // stays within [0,1] and is passed through untouched.
        //
        // Tone mapping is AUTO-EXPOSED from the frame's own brightness (a high luminance
        // percentile), so we never rely on a guessed paper-white nit value. Mapping is
        // applied on luminance with RGB scaled by the same factor, which preserves hue and
        // saturation (per-channel ACES washes colors out — the milky look we had before).
        bool isHdr = FindMaxChannel(srcAddr, rowPitch, width, height) > 1.0001f;
        float whitePoint = isHdr ? ComputeWhitePoint(srcAddr, rowPitch, width, height) : 1f;
        CaptureLog.Write($"  Convert: isHdr={isHdr} whitePoint={whitePoint:F3} toneMap={(isHdr ? "Reinhard(luma)" : "passthrough")}");

        // Reinhard-extended white scale: maps `whitePoint` luminance to ~1.0 while keeping
        // the curve monotonic and contrast-preserving in the mid-tones.
        float invWhiteSq = isHdr ? 1f / (whitePoint * whitePoint) : 0f;

        var handle = GCHandle.Alloc(outBuf, GCHandleType.Pinned);
        try
        {
            long dstAddr = handle.AddrOfPinnedObject().ToInt64();

            Parallel.For(0, height, y =>
            {
                unsafe
                {
                    ushort* srcRow = (ushort*)(srcAddr + (long)y * rowPitch);
                    byte* dstRow = (byte*)(dstAddr + (long)y * width * 4);

                    for (int x = 0; x < width; x++)
                    {
                        int si = x * 4;
                        float r = (float)BitConverter.UInt16BitsToHalf(srcRow[si]);
                        float g = (float)BitConverter.UInt16BitsToHalf(srcRow[si + 1]);
                        float b = (float)BitConverter.UInt16BitsToHalf(srcRow[si + 2]);

                        if (isHdr)
                        {
                            // Luminance-preserving Reinhard-extended tone map.
                            float l = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                            if (l > 0f)
                            {
                                float mapped = l * (1f + l * invWhiteSq) / (1f + l);
                                float k = mapped / l;
                                r *= k;
                                g *= k;
                                b *= k;
                            }
                        }

                        int di = x * 4;
                        dstRow[di] = LinearToSrgb(b);
                        dstRow[di + 1] = LinearToSrgb(g);
                        dstRow[di + 2] = LinearToSrgb(r);
                        dstRow[di + 3] = 255;
                    }
                }
            });
        }
        finally
        {
            handle.Free();
        }

        return outBuf;
    }

    /// <summary>
    /// Auto-exposure white point: the ~95th-percentile luminance of the frame, so a few
    /// blown specular highlights don't crush the rest of the image. Returns a value &gt; 1
    /// only for genuine HDR frames. Uses a coarse log-luminance histogram (single pass).
    /// </summary>
    private static float ComputeWhitePoint(long srcAddr, int rowPitch, int width, int height)
    {
        const int Bins = 256;
        const float MinLog = -4f;  // ~0.06 linear
        const float MaxLog = 6f;   // ~64 linear (scRGB)
        var bins = new int[Bins];
        long total = 0;

        unsafe
        {
            for (int y = 0; y < height; y++)
            {
                ushort* row = (ushort*)(srcAddr + (long)y * rowPitch);
                for (int x = 0; x < width; x++)
                {
                    int si = x * 4;
                    float r = (float)BitConverter.UInt16BitsToHalf(row[si]);
                    float g = (float)BitConverter.UInt16BitsToHalf(row[si + 1]);
                    float b = (float)BitConverter.UInt16BitsToHalf(row[si + 2]);
                    float l = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                    if (l <= 0f) continue;

                    float logL = MathF.Log2(l);
                    int bin = (int)((logL - MinLog) / (MaxLog - MinLog) * Bins);
                    if (bin < 0) bin = 0;
                    else if (bin >= Bins) bin = Bins - 1;
                    bins[bin]++;
                    total++;
                }
            }
        }

        if (total == 0) return 1f;

        long target = (long)(total * 0.95);
        long acc = 0;
        int pbin = Bins - 1;
        for (int i = 0; i < Bins; i++)
        {
            acc += bins[i];
            if (acc >= target) { pbin = i; break; }
        }

        float pLog = MinLog + (pbin + 0.5f) / Bins * (MaxLog - MinLog);
        float white = MathF.Pow(2f, pLog);
        return white < 1f ? 1f : white;
    }

    private static float FindMaxChannel(long srcAddr, int rowPitch, int width, int height)
    {
        object sync = new();
        float globalMax = 0f;

        Parallel.For(0, height, () => 0f,
            (y, _, local) =>
            {
                unsafe
                {
                    ushort* row = (ushort*)(srcAddr + (long)y * rowPitch);
                    for (int x = 0; x < width; x++)
                    {
                        int si = x * 4;
                        float r = (float)BitConverter.UInt16BitsToHalf(row[si]);
                        float g = (float)BitConverter.UInt16BitsToHalf(row[si + 1]);
                        float b = (float)BitConverter.UInt16BitsToHalf(row[si + 2]);
                        if (r > local) local = r;
                        if (g > local) local = g;
                        if (b > local) local = b;
                    }
                }
                return local;
            },
            local => { lock (sync) { if (local > globalMax) globalMax = local; } });

        return globalMax;
    }

    /// <summary>Encodes a linear [0,1] channel to an 8-bit sRGB value.</summary>
    private static byte LinearToSrgb(float c)
    {
        if (c <= 0f) return 0;
        if (c >= 1f) return 255;
        float s = c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
        int v = (int)(s * 255f + 0.5f);
        return (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));
    }
}
