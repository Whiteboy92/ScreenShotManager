using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ScreenShotManager.Shared.Helpers;
using ScreenShotManager.VideoDownscaler.Models;
using ScreenShotManager.VideoDownscaler.Services.Interfaces;

namespace ScreenShotManager.VideoDownscaler.Services;

/// <summary>
/// Background, single-consumer queue that downscales videos one at a time so multiple
/// selected files are processed sequentially without blocking the tray app.
/// </summary>
public sealed class DownscaleService : IDownscaleService, IDisposable
{
    private readonly IFFprobeService ffprobe;
    private readonly IFFmpegService ffmpeg;
    private readonly IToolPathProvider tools;

    private readonly Channel<DownscaleRequest> queue =
        Channel.CreateUnbounded<DownscaleRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
        });

    private readonly CancellationTokenSource cts = new();
    private Task? worker;
    private int started;

    public DownscaleService(IFFprobeService ffprobe, IFFmpegService ffmpeg, IToolPathProvider tools)
    {
        this.ffprobe = ffprobe;
        this.ffmpeg = ffmpeg;
        this.tools = tools;
    }

    public event EventHandler<DownscaleJobResult>? JobFinished;
    public event EventHandler<DownscaleRequest>? JobStarted;
    public event EventHandler<string>? Faulted;

    public void Start()
    {
        // Ensure the consumer loop is created exactly once.
        if (Interlocked.Exchange(ref started, 1) == 1)
        {
            return;
        }

        worker = Task.Run(() => ConsumeAsync(cts.Token));
    }

    public bool Enqueue(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !DownscaleConstants.IsSupported(filePath))
        {
            CaptureLog.Write($"[downscale] rejected unsupported path: {filePath}");
            return false;
        }

        var full = Path.GetFullPath(filePath);
        if (!File.Exists(full))
        {
            CaptureLog.Write($"[downscale] rejected missing file: {full}");
            return false;
        }

        var accepted = queue.Writer.TryWrite(new DownscaleRequest(full));
        if (accepted)
        {
            CaptureLog.Write($"[downscale] queued: {full}");
        }

        return accepted;
    }

    public void Stop()
    {
        queue.Writer.TryComplete();
        cts.Cancel();
    }

    private async Task ConsumeAsync(CancellationToken token)
    {
        if (!tools.ToolsAvailable)
        {
            CaptureLog.Write(
                $"[downscale] FFmpeg/FFprobe not found at {tools.FFmpegPath} / {tools.FFprobePath}");
        }

        try
        {
            await foreach (var request in queue.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                var result = await ProcessAsync(request, token).ConfigureAwait(false);
                Raise(JobFinished, result);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
        catch (Exception ex)
        {
            CaptureLog.Write($"[downscale] worker crashed: {ex}");
            Raise(Faulted, ex.Message);
        }
    }

    private async Task<DownscaleJobResult> ProcessAsync(DownscaleRequest request, CancellationToken token)
    {
        var source = request.SourcePath;
        try
        {
            Raise(JobStarted, request);
            CaptureLog.Write($"[downscale] start: {source}");

            var resolution = await ffprobe.GetResolutionAsync(source, token).ConfigureAwait(false);
            var target = ResolutionTier.NextLowerTier(resolution);

            if (target is null)
            {
                CaptureLog.Write(
                    $"[downscale] skip (already <= {ResolutionTier.Floor}): {source} [{resolution}]");
                return DownscaleJobResult.Skipped(
                    source, resolution, $"Already at or below {ResolutionTier.Floor}.");
            }

            var output = OutputPathResolver.Resolve(source);
            CaptureLog.Write($"[downscale] {resolution} -> {target.Value}: {source} => {output}");

            await ffmpeg.DownscaleAsync(source, output, target.Value, token).ConfigureAwait(false);

            CaptureLog.Write($"[downscale] done: {output}");
            return DownscaleJobResult.Completed(source, resolution, target.Value, output);
        }
        catch (OperationCanceledException)
        {
            CaptureLog.Write($"[downscale] cancelled: {source}");
            return DownscaleJobResult.Failed(source, "Cancelled.");
        }
        catch (Exception ex)
        {
            CaptureLog.Write($"[downscale] FAILED: {source} :: {ex.Message}");
            return DownscaleJobResult.Failed(source, ex.Message);
        }
    }

    private static void Raise<T>(EventHandler<T>? handler, T arg)
    {
        try
        {
            handler?.Invoke(null, arg);
        }
        catch (Exception ex)
        {
            CaptureLog.Write($"[downscale] event handler threw: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Stop();
        try
        {
            worker?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignore shutdown races
        }

        cts.Dispose();
    }
}
