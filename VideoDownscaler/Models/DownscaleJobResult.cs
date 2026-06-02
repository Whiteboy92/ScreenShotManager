namespace ScreenShotManager.VideoDownscaler.Models;

/// <summary>Terminal state of a single downscale job.</summary>
public enum DownscaleOutcome
{
    /// <summary>File was downscaled and written successfully.</summary>
    Completed,

    /// <summary>Source already at or below the lowest tier — nothing to do.</summary>
    Skipped,

    /// <summary>Probe or encode failed; see <see cref="DownscaleJobResult.Message"/>.</summary>
    Failed,
}

/// <summary>
/// Outcome of processing one <see cref="DownscaleRequest"/>.
/// </summary>
public sealed record DownscaleJobResult(
    string SourcePath,
    DownscaleOutcome Outcome,
    VideoResolution? SourceResolution = null,
    VideoResolution? TargetResolution = null,
    string? Message = null)
{
    public static DownscaleJobResult Completed(
        string source, VideoResolution from, VideoResolution to) =>
        new(source, DownscaleOutcome.Completed, from, to);

    public static DownscaleJobResult Skipped(string source, VideoResolution res, string reason) =>
        new(source, DownscaleOutcome.Skipped, res, Message: reason);

    public static DownscaleJobResult Failed(string source, string error) =>
        new(source, DownscaleOutcome.Failed, Message: error);
}
