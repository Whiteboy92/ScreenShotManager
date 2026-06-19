using System;
using System.Threading;
using System.Threading.Tasks;
using ScreenShotManager.VideoDownscaler.Models;

namespace ScreenShotManager.VideoDownscaler.Services.Interfaces;

/// <summary>
/// Reads video metadata via the bundled FFprobe binary.
/// </summary>
public interface IFFprobeService
{
    /// <summary>
    /// Returns the resolution of the first video stream in <paramref name="filePath"/>.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">No video stream / probe failed.</exception>
    Task<VideoResolution> GetResolutionAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the container duration, or <see cref="TimeSpan.Zero"/> if it cannot be determined
    /// (progress reporting is simply skipped in that case — never throws on a missing value).
    /// </summary>
    Task<TimeSpan> GetDurationAsync(string filePath, CancellationToken cancellationToken = default);
}
