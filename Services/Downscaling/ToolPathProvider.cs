using System;
using System.IO;
using ScreenShotManager.Services.Interfaces;

namespace ScreenShotManager.Services.Downscaling;

/// <summary>
/// Locates the bundled FFmpeg/FFprobe binaries. Probes the app base directory first
/// (publish/output layout) then walks up a few parents to find the repo <c>Tools\</c>
/// folder during development.
/// </summary>
public sealed class ToolPathProvider : IToolPathProvider
{
    private const string ToolsFolder = "Tools";

    public ToolPathProvider()
    {
        FFmpegPath = Resolve("ffmpeg.exe");
        FFprobePath = Resolve("ffprobe.exe");
    }

    public string FFmpegPath { get; }

    public string FFprobePath { get; }

    public bool ToolsAvailable => File.Exists(FFmpegPath) && File.Exists(FFprobePath);

    private static string Resolve(string exeName)
    {
        var dir = AppContext.BaseDirectory;

        // Walk up: output dir, then bin\Debug\netX -> project root during development.
        for (var i = 0; i < 6 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, ToolsFolder, exeName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        // Fall back to the expected output location even if missing, so callers get a clear path.
        return Path.Combine(AppContext.BaseDirectory, ToolsFolder, exeName);
    }
}
