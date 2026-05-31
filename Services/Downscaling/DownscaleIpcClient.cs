using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using ScreenShotManager.Helpers;
using ScreenShotManager.Models.Downscaling;
using ScreenShotManager.Services.Interfaces;

namespace ScreenShotManager.Services.Downscaling;

/// <summary>
/// Forwards file paths to the primary instance's pipe. Used by short-lived
/// <c>--downscale</c> launches when an instance is already running.
/// </summary>
public sealed class DownscaleIpcClient : IDownscaleIpcClient
{
    private const int ConnectTimeoutMs = 2000;

    public async Task<bool> TrySendAsync(
        IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                DownscaleConstants.PipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous);

            await client.ConnectAsync(ConnectTimeoutMs, cancellationToken).ConfigureAwait(false);

            await using var writer = new StreamWriter(client, System.Text.Encoding.UTF8)
            {
                AutoFlush = true,
            };

            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                await writer.WriteLineAsync(path.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (TimeoutException)
        {
            // No server listening => no instance running.
            return false;
        }
        catch (Exception ex)
        {
            CaptureLog.Write($"[ipc] client send failed: {ex.Message}");
            return false;
        }
    }
}
