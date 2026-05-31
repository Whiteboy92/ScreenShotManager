using System.Threading;
using System.Threading.Tasks;
using ScreenShotManager.Models.Downscaling;

namespace ScreenShotManager.Services.Interfaces;

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
}
