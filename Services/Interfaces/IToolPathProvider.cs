namespace ScreenShotManager.Services.Interfaces;

/// <summary>
/// Resolves absolute paths to the bundled FFmpeg/FFprobe binaries (Tools\ folder).
/// Never relies on a system-wide install.
/// </summary>
public interface IToolPathProvider
{
    /// <summary>Absolute path to <c>ffmpeg.exe</c>.</summary>
    string FFmpegPath { get; }

    /// <summary>Absolute path to <c>ffprobe.exe</c>.</summary>
    string FFprobePath { get; }

    /// <summary>True when both binaries exist on disk.</summary>
    bool ToolsAvailable { get; }
}
