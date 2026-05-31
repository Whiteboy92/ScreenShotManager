using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using ScreenShotManager.Helpers;
using ScreenShotManager.Models.Downscaling;
using ScreenShotManager.Services.Interfaces;

namespace ScreenShotManager.Services.Downscaling;

/// <summary>
/// Named-pipe server owned by the primary instance. Accepts one connection at a time,
/// reads newline-delimited file paths until the client closes the pipe, and raises
/// <see cref="PathReceived"/> for each. Loops forever until disposed.
/// </summary>
public sealed class DownscaleIpcServer : IDownscaleIpcServer
{
    private readonly CancellationTokenSource cts = new();
    private Task? listenLoop;
    private int started;

    public event EventHandler<string>? PathReceived;

    public void Start()
    {
        if (Interlocked.Exchange(ref started, 1) == 1)
        {
            return;
        }

        listenLoop = Task.Run(() => ListenAsync(cts.Token));
        CaptureLog.Write($"[ipc] server listening on {DownscaleConstants.PipeName}");
    }

    private async Task ListenAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    DownscaleConstants.PipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                await ReadConnectionAsync(server, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                CaptureLog.Write($"[ipc] server error: {ex.Message}");
                // Avoid a tight failure loop.
                try { await Task.Delay(250, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task ReadConnectionAsync(NamedPipeServerStream server, CancellationToken token)
    {
        using var reader = new StreamReader(server, System.Text.Encoding.UTF8);
        string? line;
        while ((line = await reader.ReadLineAsync(token).ConfigureAwait(false)) != null)
        {
            var path = line.Trim();
            if (path.Length == 0)
            {
                continue;
            }

            CaptureLog.Write($"[ipc] received: {path}");
            try
            {
                PathReceived?.Invoke(this, path);
            }
            catch (Exception ex)
            {
                CaptureLog.Write($"[ipc] PathReceived handler threw: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        try
        {
            cts.Cancel();
            listenLoop?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // ignore shutdown races
        }

        cts.Dispose();
    }
}
