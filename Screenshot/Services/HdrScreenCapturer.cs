using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// HDR content to white), this reads the composed surface and tone-maps HDR outputs
/// down to SDR — the same idea the Windows Snipping Tool uses, so bright HDR areas no
/// longer come out "ultra white".
///
/// Performance: DXGI devices and per-output duplication handles are created once and
/// reused across captures (they are expensive to spin up). SDR outputs are duplicated
/// as native 8-bit BGRA (a straight row copy — no tone-map), and only genuine HDR
/// outputs take the fp16 scRGB path. When the desktop hasn't changed, the duplication
/// acquire times out fast and the last decoded frame is reused instead of stalling.
///
/// Assumptions / limitations:
/// - Coordinates are physical pixels; the pipeline assumes 100% / system DPI scaling.
/// - Display rotation is not handled (landscape only).
/// - HDR→SDR is an approximation (auto-exposed Reinhard on luminance).
/// </summary>
internal sealed class HdrScreenCapturer : IDisposable
{
    // Hot-path acquire: one short attempt. A timeout means the desktop is unchanged,
    // so the previously decoded frame is reused (see AcquireAndDecode).
    private const int HotAcquireTimeoutMs = 50;

    // Priming (first frame after a duplication is created): the initial AcquireNextFrame
    // can legitimately time out before the first desktop update lands, so retry longer.
    private const int PrimeAcquireTimeoutMs = 200;
    private const int PrimeAttempts = 8;

    // half-bits (scRGB) → sRGB byte, precomputed once. Turns the SDR fp16 passthrough
    // into a pure table lookup (no per-pixel Pow / Half decode).
    private static readonly byte[] Fp16SrgbLut = BuildFp16SrgbLut();

    private readonly object captureLock = new();

    private IDXGIFactory1? factory;
    private readonly List<OutputDuplicator> duplicators = new();
    private bool initialized;

    // Reused destination Bitmap sized to the virtual desktop. GDI/unmanaged, so no LOH
    // churn; recreated only when the desktop geometry changes.
    private Bitmap? destination;
    private int destWidth, destHeight, destOriginX, destOriginY;

    /// <summary>
    /// One duplicated output: its device/context, duplication handle, staging texture and a
    /// persistent decoded-BGRA buffer. All reused across captures; rebuilt on access-lost.
    /// </summary>
    private sealed class OutputDuplicator : IDisposable
    {
        public required ID3D11Device Device { get; init; }
        public required ID3D11DeviceContext Context { get; init; }
        public required IDXGIOutput6 Output { get; init; }

        public IDXGIOutputDuplication? Duplication;
        public ID3D11Texture2D? Staging;
        public byte[] Bgra = Array.Empty<byte>();
        public bool HasFrame;

        public int Left, Top, Width, Height;
        public Format Format;

        public void DisposeDuplication()
        {
            Staging?.Dispose();
            Staging = null;
            Duplication?.Dispose();
            Duplication = null;
            HasFrame = false;
        }

        public void Dispose()
        {
            DisposeDuplication();
            Output.Dispose();
            Context.Dispose();
            Device.Dispose();
        }
    }

    /// <summary>
    /// Builds the DXGI factory, devices and per-output duplication if not already done, and
    /// captures one frame from every output to prime the caches. Safe to call repeatedly.
    /// </summary>
    public void WarmUp()
    {
        lock (captureLock)
        {
            EnsureInitialized();
            foreach (var dup in duplicators)
            {
                try { AcquireAndDecode(dup); }
                catch (Exception ex) { CaptureLog.Write($"  WarmUp acquire failed on {dup.Output.Description.DeviceName}: {ex.Message}"); }
            }
        }
    }

    /// <summary>
    /// Captures every attached output and stitches them into one bitmap.
    /// <paramref name="originX"/>/<paramref name="originY"/> give the top-left of the
    /// virtual desktop in physical pixels (matches WPF virtual-screen origin at 100% DPI).
    /// </summary>
    public Bitmap CaptureVirtualDesktop(out int originX, out int originY)
    {
        lock (captureLock)
        {
            var sw = Stopwatch.StartNew();

            EnsureInitialized();
            long tInit = sw.ElapsedMilliseconds;

            if (duplicators.Count == 0)
            {
                throw new InvalidOperationException("DXGI Desktop Duplication returned no outputs.");
            }

            int reused = 0;
            foreach (var dup in duplicators)
            {
                if (!AcquireAndDecode(dup))
                {
                    reused++;
                }
            }
            long tAcquire = sw.ElapsedMilliseconds;

            var bitmap = CompositeToBitmap(out originX, out originY);
            long tComposite = sw.ElapsedMilliseconds;

            CaptureLog.Write($"  Timing(ms): init={tInit} acquire+decode={tAcquire - tInit} composite={tComposite - tAcquire} total={tComposite} outputs={duplicators.Count} reusedFrames={reused}");
            return bitmap;
        }
    }

    private void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

        for (int ai = 0; factory.EnumAdapters1((uint)ai, out IDXGIAdapter1 adapter).Success; ai++)
        {
            using (adapter)
            {
                InitAdapter(adapter);
            }
        }

        initialized = true;
        CaptureLog.Write($"  Initialized DXGI. outputs={duplicators.Count}");
    }

    private void InitAdapter(IDXGIAdapter1 adapter)
    {
        // One device+context shared by every output on this adapter.
        ID3D11Device? device = null;
        try
        {
            D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                null, out device).CheckError();
        }
        catch (Exception ex)
        {
            device?.Dispose();
            CaptureLog.Write($"  D3D11CreateDevice failed: {ex.Message}");
            return;
        }

        var context = device!.ImmediateContext;
        bool deviceUsed = false;

        for (int oi = 0; adapter.EnumOutputs((uint)oi, out IDXGIOutput output).Success; oi++)
        {
            IDXGIOutput6? output6 = null;
            bool keep = false;
            try
            {
                using (output)
                {
                    try
                    {
                        output6 = output.QueryInterface<IDXGIOutput6>();
                    }
                    catch
                    {
                        continue; // pre-Win10-1703 output; skip
                    }

                    if (!output6.Description.AttachedToDesktop)
                    {
                        continue;
                    }

                    // Each output needs its own device so duplication survives independently;
                    // sharing one device across outputs on an adapter is allowed by the API,
                    // so reuse this adapter's device for the first output and create siblings
                    // as needed.
                    ID3D11Device outDevice;
                    ID3D11DeviceContext outContext;
                    if (!deviceUsed)
                    {
                        outDevice = device;
                        outContext = context;
                        deviceUsed = true;
                    }
                    else
                    {
                        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                            null, out var sib).CheckError();
                        outDevice = sib!;
                        outContext = sib!.ImmediateContext;
                    }

                    var dup = new OutputDuplicator
                    {
                        Device = outDevice,
                        Context = outContext,
                        Output = output6,
                    };

                    if (RebuildDuplication(dup))
                    {
                        duplicators.Add(dup);
                        keep = true;
                    }
                    else if (!ReferenceEquals(outDevice, device))
                    {
                        outContext.Dispose();
                        outDevice.Dispose();
                    }
                }
            }
            finally
            {
                if (!keep)
                {
                    output6?.Dispose();
                }
            }
        }

        if (!deviceUsed)
        {
            context.Dispose();
            device.Dispose();
        }
    }

    /// <summary>
    /// (Re)creates the duplication handle and staging texture for an output and refreshes its
    /// geometry. Returns false if the output cannot currently be duplicated (e.g. protected
    /// content) so the caller can drop it.
    /// </summary>
    private bool RebuildDuplication(OutputDuplicator dup)
    {
        dup.DisposeDuplication();

        var desc = dup.Output.Description;
        var coords = desc.DesktopCoordinates;
        dup.Left = coords.Left;
        dup.Top = coords.Top;
        dup.Width = coords.Right - coords.Left;
        dup.Height = coords.Bottom - coords.Top;

        var colorSpace = dup.Output.Description1.ColorSpace;
        CaptureLog.Write($"  Output {desc.DeviceName} rect=({coords.Left},{coords.Top},{coords.Right},{coords.Bottom}) colorSpace={colorSpace}");

        try
        {
            // Offer native 8-bit first (SDR desktops copy without conversion) and fp16 for
            // HDR desktops. DXGI picks whichever needs no conversion for the current mode.
            dup.Duplication = dup.Output.DuplicateOutput1(dup.Device, 0,
                new[] { Format.B8G8R8A8_UNorm, Format.R16G16B16A16_Float });
        }
        catch (Exception ex)
        {
            CaptureLog.Write($"  DuplicateOutput1 FAILED: {ex.GetType().Name}: {ex.Message}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Acquires the latest frame for an output and decodes it into the output's persistent
    /// BGRA buffer. Returns true if a fresh frame was decoded; false if the acquire timed out
    /// and the previously decoded frame was kept (desktop unchanged) — a no-op fast path.
    /// </summary>
    private bool AcquireAndDecode(OutputDuplicator dup)
    {
        if (dup.Duplication == null && !RebuildDuplication(dup))
        {
            return false;
        }

        bool priming = !dup.HasFrame;
        int attempts = priming ? PrimeAttempts : 1;
        int timeout = priming ? PrimeAcquireTimeoutMs : HotAcquireTimeoutMs;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            IDXGIResource? frameResource = null;
            Result result = dup.Duplication!.AcquireNextFrame((uint)timeout, out _, out frameResource);

            if (result == Vortice.DXGI.ResultCode.WaitTimeout)
            {
                frameResource?.Dispose();
                if (priming)
                {
                    continue; // no first frame yet; keep waiting
                }
                return false; // unchanged desktop → reuse cached frame
            }

            if (result.Failure || frameResource == null)
            {
                frameResource?.Dispose();
                CaptureLog.Write($"  AcquireNextFrame failed ({result}); rebuilding duplication");
                if (RebuildDuplication(dup))
                {
                    priming = true;
                    attempts = PrimeAttempts;
                    timeout = PrimeAcquireTimeoutMs;
                    continue;
                }
                return false;
            }

            try
            {
                DecodeFrame(dup, frameResource);
                dup.HasFrame = true;
                return true;
            }
            finally
            {
                frameResource.Dispose();
                dup.Duplication!.ReleaseFrame();
            }
        }

        return dup.HasFrame; // primed earlier, just no new frame within the window
    }

    private void DecodeFrame(OutputDuplicator dup, IDXGIResource frameResource)
    {
        using var texture = frameResource.QueryInterface<ID3D11Texture2D>();
        var td = texture.Description;

        // Resolution/format change (or first frame): (re)build staging + buffer to match.
        if (dup.Staging == null || dup.Format != td.Format ||
            dup.Width != (int)td.Width || dup.Height != (int)td.Height)
        {
            dup.Staging?.Dispose();
            dup.Staging = dup.Device.CreateTexture2D(new Texture2DDescription
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
            });
            dup.Format = td.Format;
            dup.Width = (int)td.Width;
            dup.Height = (int)td.Height;
            dup.Bgra = new byte[dup.Width * dup.Height * 4];
        }

        dup.Context.CopyResource(dup.Staging, texture);

        MappedSubresource map = dup.Context.Map(dup.Staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            if (td.Format == Format.R16G16B16A16_Float)
            {
                DecodeFp16(map.DataPointer, (int)map.RowPitch, dup.Width, dup.Height, dup.Bgra);
            }
            else
            {
                DecodeBgra8(map.DataPointer, (int)map.RowPitch, dup.Width, dup.Height, dup.Bgra);
            }
        }
        finally
        {
            dup.Context.Unmap(dup.Staging, 0);
        }
    }

    /// <summary>Native 8-bit BGRA: straight row copy (SDR fast path, no tone-map).</summary>
    private static void DecodeBgra8(IntPtr src, int rowPitch, int width, int height, byte[] dst)
    {
        int dstStride = width * 4;
        long srcAddr = src.ToInt64();
        var handle = GCHandle.Alloc(dst, GCHandleType.Pinned);
        try
        {
            long dstAddr = handle.AddrOfPinnedObject().ToInt64();
            Parallel.For(0, height, y =>
            {
                unsafe
                {
                    Buffer.MemoryCopy(
                        (void*)(srcAddr + (long)y * rowPitch),
                        (void*)(dstAddr + (long)y * dstStride),
                        dstStride, dstStride);
                }
            });
        }
        finally
        {
            handle.Free();
        }
    }

    private static void DecodeFp16(IntPtr src, int rowPitch, int width, int height, byte[] dst)
    {
        long srcAddr = src.ToInt64();

        // Detect HDR + auto-exposure white point in one subsampled pass. scRGB (linear,
        // 1.0 == 80 nits): any channel > 1 means real HDR highlights; SDR stays in [0,1]
        // and is decoded via the sRGB LUT with no math.
        var (maxChannel, whitePoint) = AnalyzeFp16(srcAddr, rowPitch, width, height);
        bool isHdr = maxChannel > 1.0001f;
        float invWhiteSq = isHdr ? 1f / (whitePoint * whitePoint) : 0f;
        CaptureLog.Write($"  fp16 decode: isHdr={isHdr} whitePoint={whitePoint:F3}");

        int dstStride = width * 4;
        var handle = GCHandle.Alloc(dst, GCHandleType.Pinned);
        try
        {
            long dstAddr = handle.AddrOfPinnedObject().ToInt64();

            Parallel.For(0, height, y =>
            {
                unsafe
                {
                    ushort* srcRow = (ushort*)(srcAddr + (long)y * rowPitch);
                    byte* dstRow = (byte*)(dstAddr + (long)y * dstStride);

                    if (!isHdr)
                    {
                        // Passthrough: pure LUT lookups on the raw half bits.
                        for (int x = 0; x < width; x++)
                        {
                            int si = x * 4;
                            int di = x * 4;
                            dstRow[di] = Fp16SrgbLut[srcRow[si + 2]];     // B
                            dstRow[di + 1] = Fp16SrgbLut[srcRow[si + 1]]; // G
                            dstRow[di + 2] = Fp16SrgbLut[srcRow[si]];     // R
                            dstRow[di + 3] = 255;
                        }
                        return;
                    }

                    for (int x = 0; x < width; x++)
                    {
                        int si = x * 4;
                        float r = (float)BitConverter.UInt16BitsToHalf(srcRow[si]);
                        float g = (float)BitConverter.UInt16BitsToHalf(srcRow[si + 1]);
                        float b = (float)BitConverter.UInt16BitsToHalf(srcRow[si + 2]);

                        // Luminance-preserving Reinhard-extended tone map (keeps hue).
                        float l = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                        if (l > 0f)
                        {
                            float mapped = l * (1f + l * invWhiteSq) / (1f + l);
                            float k = mapped / l;
                            r *= k;
                            g *= k;
                            b *= k;
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
    }

    /// <summary>
    /// Single subsampled pass returning (max channel, ~95th-percentile luminance). Subsampling
    /// (every other row and column) is statistically identical for region-sized HDR highlights
    /// and quarters the work versus two full-frame passes.
    /// </summary>
    private static (float max, float whitePoint) AnalyzeFp16(long srcAddr, int rowPitch, int width, int height)
    {
        const int Bins = 256;
        const float MinLog = -4f;
        const float MaxLog = 6f;

        var bins = new int[Bins];
        long total = 0;
        float max = 0f;

        unsafe
        {
            for (int y = 0; y < height; y += 2)
            {
                ushort* row = (ushort*)(srcAddr + (long)y * rowPitch);
                for (int x = 0; x < width; x += 2)
                {
                    int si = x * 4;
                    float r = (float)BitConverter.UInt16BitsToHalf(row[si]);
                    float g = (float)BitConverter.UInt16BitsToHalf(row[si + 1]);
                    float b = (float)BitConverter.UInt16BitsToHalf(row[si + 2]);

                    if (r > max) max = r;
                    if (g > max) max = g;
                    if (b > max) max = b;

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

        if (total == 0)
        {
            return (max, 1f);
        }

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
        return (max, white < 1f ? 1f : white);
    }

    /// <summary>
    /// Blits every output's decoded buffer into one virtual-desktop bitmap. Reuses the
    /// destination bitmap across captures unless the geometry changed.
    /// </summary>
    private Bitmap CompositeToBitmap(out int originX, out int originY)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var d in duplicators)
        {
            minX = Math.Min(minX, d.Left);
            minY = Math.Min(minY, d.Top);
            maxX = Math.Max(maxX, d.Left + d.Width);
            maxY = Math.Max(maxY, d.Top + d.Height);
        }

        originX = minX;
        originY = minY;
        int width = maxX - minX;
        int height = maxY - minY;

        if (destination == null || destWidth != width || destHeight != height ||
            destOriginX != minX || destOriginY != minY)
        {
            destination?.Dispose();
            destination = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            destWidth = width;
            destHeight = height;
            destOriginX = minX;
            destOriginY = minY;
        }

        var data = destination.LockBits(new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            foreach (var d in duplicators)
            {
                if (d.Bgra.Length == 0)
                {
                    continue;
                }

                int dx = d.Left - minX;
                int dy = d.Top - minY;
                int rowBytes = d.Width * 4;

                for (int y = 0; y < d.Height; y++)
                {
                    IntPtr dest = IntPtr.Add(data.Scan0, (dy + y) * data.Stride + dx * 4);
                    Marshal.Copy(d.Bgra, y * rowBytes, dest, rowBytes);
                }
            }
        }
        finally
        {
            destination.UnlockBits(data);
        }

        // Hand back a copy so callers can crop/keep it without racing the next capture's
        // reuse of the destination bitmap.
        return (Bitmap)destination.Clone();
    }

    private static byte[] BuildFp16SrgbLut()
    {
        var lut = new byte[65536];
        for (int i = 0; i < 65536; i++)
        {
            float v = (float)BitConverter.UInt16BitsToHalf((ushort)i);
            lut[i] = LinearToSrgb(v);
        }
        return lut;
    }

    /// <summary>Encodes a linear [0,1] channel to an 8-bit sRGB value.</summary>
    private static byte LinearToSrgb(float c)
    {
        if (!(c > 0f)) return 0; // also catches NaN
        if (c >= 1f) return 255;
        float s = c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
        int v = (int)(s * 255f + 0.5f);
        return (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));
    }

    public void Dispose()
    {
        lock (captureLock)
        {
            foreach (var d in duplicators)
            {
                try { d.Dispose(); } catch { /* best effort */ }
            }
            duplicators.Clear();

            destination?.Dispose();
            destination = null;

            factory?.Dispose();
            factory = null;
            initialized = false;
        }
    }
}
