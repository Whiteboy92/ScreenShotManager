using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenShotManager.VideoDownscaler.Services.Interfaces;

/// <summary>
/// Client side of the downscale IPC: forwards file paths to the running primary instance.
/// </summary>
public interface IDownscaleIpcClient
{
    /// <summary>
    /// Sends <paramref name="paths"/> to the primary instance's pipe.
    /// Returns false when no server is listening (no instance running).
    /// </summary>
    Task<bool> TrySendAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default);
}
