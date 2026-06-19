using System;
using System.Threading;
using System.Threading.Tasks;
using ScreenShotManager.VideoDownscaler.Models;

namespace ScreenShotManager.VideoDownscaler.Services.Interfaces;

/// <summary>
/// Wraps the bundled FFmpeg binary to perform downscaling encodes.
/// </summary>
public interface IFFmpegService
{
    /// <summary>
    /// Re-encodes <paramref name="inputPath"/> into <paramref name="outputPath"/>, scaling the
    /// frame to fit within <paramref name="target"/> while preserving aspect ratio. Prefers a
    /// hardware encoder (NVENC/QSV/AMF) when available, falling back to libx264 (crf 23, preset
    /// veryfast). Audio is copied.
    /// </summary>
    /// <param name="duration">
    /// Source duration used to compute <paramref name="progress"/>; pass <see cref="TimeSpan.Zero"/>
    /// when unknown to disable progress reporting.
    /// </param>
    /// <param name="progress">Optional 0..1 progress sink, reported as encoding advances.</param>
    /// <exception cref="System.InvalidOperationException">FFmpeg exited non-zero.</exception>
    Task DownscaleAsync(
        string inputPath,
        string outputPath,
        VideoResolution target,
        TimeSpan duration,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
