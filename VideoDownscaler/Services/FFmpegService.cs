using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ScreenShotManager.Shared.Helpers;
using ScreenShotManager.VideoDownscaler.Models;
using ScreenShotManager.VideoDownscaler.Services.Interfaces;

namespace ScreenShotManager.VideoDownscaler.Services;

/// <summary>
/// Performs the downscale encode with the bundled FFmpeg binary. Prefers a hardware encoder
/// (NVENC/QSV/AMF) for speed and falls back to libx264 when none is usable.
/// </summary>
public sealed class FFmpegService : IFFmpegService
{
    private const string SoftwareEncoder = "libx264";

    private readonly IToolPathProvider tools;
    private readonly SemaphoreSlim encoderLock = new(1, 1);
    private string? cachedEncoder;

    public FFmpegService(IToolPathProvider tools)
    {
        this.tools = tools;
    }

    public async Task DownscaleAsync(
        string inputPath,
        string outputPath,
        VideoResolution target,
        TimeSpan duration,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var encoder = await GetPreferredEncoderAsync(cancellationToken).ConfigureAwait(false);

        var result = await RunEncodeAsync(
            encoder, inputPath, outputPath, target, duration, progress, cancellationToken)
            .ConfigureAwait(false);

        // A hardware encoder can be advertised by the build yet fail at runtime (no GPU, stale
        // driver, busy session). Retry once on CPU and stop preferring HW for the rest of the session.
        if (!result.Success && encoder != SoftwareEncoder)
        {
            CaptureLog.Write(
                $"[downscale] {encoder} failed (exit {result.ExitCode}); retrying {SoftwareEncoder}. {Tail(result.StdErr)}");
            cachedEncoder = SoftwareEncoder;
            result = await RunEncodeAsync(
                SoftwareEncoder, inputPath, outputPath, target, duration, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!result.Success)
        {
            // Don't leave a half-written file behind on failure.
            TryDelete(outputPath);
            throw new InvalidOperationException(
                $"ffmpeg exited {result.ExitCode}: {Tail(result.StdErr)}");
        }
    }

    private async Task<ProcessResult> RunEncodeAsync(
        string encoder,
        string inputPath,
        string outputPath,
        VideoResolution target,
        TimeSpan duration,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var args = BuildArguments(encoder, inputPath, outputPath, target);

        Action<string>? onLine = null;
        if (progress != null && duration > TimeSpan.Zero)
        {
            var totalSeconds = duration.TotalSeconds;
            onLine = line => ReportProgress(line, totalSeconds, progress);
        }

        return await ProcessRunner.RunAsync(tools.FFmpegPath, args, cancellationToken, onLine)
            .ConfigureAwait(false);
    }

    /// <summary>Parses FFmpeg <c>-progress</c> "out_time=HH:MM:SS.us" lines into a 0..1 fraction.</summary>
    private static void ReportProgress(string line, double totalSeconds, IProgress<double> progress)
    {
        const string key = "out_time=";
        if (!line.StartsWith(key, StringComparison.Ordinal))
        {
            return;
        }

        var value = line.AsSpan(key.Length).Trim();
        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var elapsed))
        {
            progress.Report(Math.Clamp(elapsed.TotalSeconds / totalSeconds, 0d, 1d));
        }
    }

    private async Task<string> GetPreferredEncoderAsync(CancellationToken cancellationToken)
    {
        if (cachedEncoder != null)
        {
            return cachedEncoder;
        }

        await encoderLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cachedEncoder ??= await DetectEncoderAsync(cancellationToken).ConfigureAwait(false);
            return cachedEncoder;
        }
        finally
        {
            encoderLock.Release();
        }
    }

    private async Task<string> DetectEncoderAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await ProcessRunner.RunAsync(
                tools.FFmpegPath, "-hide_banner -encoders", cancellationToken).ConfigureAwait(false);

            // Preference order: NVIDIA, Intel QuickSync, AMD. Presence in the build is necessary
            // but not sufficient — DownscaleAsync falls back to libx264 if the chosen one fails.
            foreach (var encoder in new[] { "h264_nvenc", "h264_qsv", "h264_amf" })
            {
                if (result.StdOut.Contains(encoder, StringComparison.Ordinal))
                {
                    CaptureLog.Write($"[downscale] hardware encoder available: {encoder}");
                    return encoder;
                }
            }
        }
        catch (Exception ex)
        {
            CaptureLog.Write($"[downscale] encoder probe failed: {ex.Message}");
        }

        CaptureLog.Write($"[downscale] using software encoder: {SoftwareEncoder}");
        return SoftwareEncoder;
    }

    private static string BuildArguments(
        string encoder, string inputPath, string outputPath, VideoResolution target)
    {
        var tw = target.Width.ToString(CultureInfo.InvariantCulture);
        var th = target.Height.ToString(CultureInfo.InvariantCulture);

        // Fit inside the target box preserving aspect ratio; force_original_aspect_ratio=decrease.
        // scale=...:force_divisible_by=2 keeps dimensions even (required by yuv420p encoders).
        var vf = $"scale={tw}:{th}:force_original_aspect_ratio=decrease:force_divisible_by=2";

        return string.Join(' ',
            "-y",
            "-hide_banner",
            "-nostats",
            $"-i \"{inputPath}\"",
            $"-vf \"{vf}\"",
            EncoderArgs(encoder),
            "-c:a copy",
            "-progress pipe:1",
            $"\"{outputPath}\"");
    }

    private static string EncoderArgs(string encoder) => encoder switch
    {
        "h264_nvenc" => "-c:v h264_nvenc -preset p5 -rc vbr -cq 23",
        "h264_qsv" => "-c:v h264_qsv -global_quality 23 -preset veryfast",
        "h264_amf" => "-c:v h264_amf -rc cqp -qp_i 23 -qp_p 23 -quality balanced",
        _ => "-c:v libx264 -crf 23 -preset veryfast",
    };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private static string Tail(string text, int maxLines = 6)
    {
        var lines = text.TrimEnd().Split('\n');
        var start = Math.Max(0, lines.Length - maxLines);
        return string.Join('\n', lines[start..]).Trim();
    }
}
