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
    /// frame to fit within <paramref name="target"/> while preserving aspect ratio
    /// (libx264, crf 23, preset medium, audio copied).
    /// </summary>
    /// <exception cref="System.InvalidOperationException">FFmpeg exited non-zero.</exception>
    Task DownscaleAsync(
        string inputPath,
        string outputPath,
        VideoResolution target,
        CancellationToken cancellationToken = default);
}
