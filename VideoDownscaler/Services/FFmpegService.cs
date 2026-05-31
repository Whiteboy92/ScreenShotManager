using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ScreenShotManager.VideoDownscaler.Models;
using ScreenShotManager.VideoDownscaler.Services.Interfaces;

namespace ScreenShotManager.VideoDownscaler.Services;

/// <summary>
/// Performs the downscale encode with the bundled FFmpeg binary.
/// </summary>
public sealed class FFmpegService : IFFmpegService
{
    private readonly IToolPathProvider tools;

    public FFmpegService(IToolPathProvider tools)
    {
        this.tools = tools;
    }

    public async Task DownscaleAsync(
        string inputPath,
        string outputPath,
        VideoResolution target,
        CancellationToken cancellationToken = default)
    {
        var args = BuildArguments(inputPath, outputPath, target);

        var result = await ProcessRunner.RunAsync(tools.FFmpegPath, args, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            // Don't leave a half-written file behind on failure.
            TryDelete(outputPath);
            throw new InvalidOperationException(
                $"ffmpeg exited {result.ExitCode}: {Tail(result.StdErr)}");
        }
    }

    private static string BuildArguments(string inputPath, string outputPath, VideoResolution target)
    {
        var tw = target.Width.ToString(CultureInfo.InvariantCulture);
        var th = target.Height.ToString(CultureInfo.InvariantCulture);

        // Fit inside the target box preserving aspect ratio; force_original_aspect_ratio=decrease.
        // scale=...:force_divisible_by=2 keeps dimensions even (required by libx264/yuv420p).
        var vf = $"scale={tw}:{th}:force_original_aspect_ratio=decrease:force_divisible_by=2";

        return string.Join(' ',
            "-y",
            "-hide_banner",
            $"-i \"{inputPath}\"",
            $"-vf \"{vf}\"",
            "-c:v libx264",
            "-crf 23",
            "-preset medium",
            "-c:a copy",
            $"\"{outputPath}\"");
    }

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
