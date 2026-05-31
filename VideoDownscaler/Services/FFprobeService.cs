using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ScreenShotManager.Shared.Helpers;
using ScreenShotManager.VideoDownscaler.Models;
using ScreenShotManager.VideoDownscaler.Services.Interfaces;

namespace ScreenShotManager.VideoDownscaler.Services;

/// <summary>
/// Reads the first video stream's resolution using the bundled FFprobe binary.
/// </summary>
public sealed class FFprobeService : IFFprobeService
{
    private readonly IToolPathProvider tools;

    public FFprobeService(IToolPathProvider tools)
    {
        this.tools = tools;
    }

    public async Task<VideoResolution> GetResolutionAsync(
        string filePath, CancellationToken cancellationToken = default)
    {
        // -v error          : quiet
        // -select_streams v:0: first video stream only
        // -show_entries ...  : just width/height
        // -of json           : machine-readable
        var args =
            $"-v error -select_streams v:0 -show_entries stream=width,height -of json \"{filePath}\"";

        var result = await ProcessRunner.RunAsync(tools.FFprobePath, args, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"ffprobe exited {result.ExitCode}: {result.StdErr.Trim()}");
        }

        return Parse(result.StdOut, filePath);
    }

    private static VideoResolution Parse(string json, string filePath)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("streams", out var streams) ||
                streams.GetArrayLength() == 0)
            {
                throw new InvalidOperationException($"No video stream found in '{filePath}'.");
            }

            var stream = streams[0];
            var width = ReadInt(stream, "width");
            var height = ReadInt(stream, "height");

            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException(
                    $"Invalid dimensions {width}x{height} in '{filePath}'.");
            }

            return new VideoResolution(width, height);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Could not parse ffprobe output for '{filePath}': {ex.Message}", ex);
        }
    }

    private static int ReadInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop))
        {
            return 0;
        }

        // ffprobe may emit numbers or numeric strings depending on version.
        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetInt32(),
            JsonValueKind.String when int.TryParse(
                prop.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) => v,
            _ => 0,
        };
    }
}
