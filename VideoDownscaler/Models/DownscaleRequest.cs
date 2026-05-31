namespace ScreenShotManager.VideoDownscaler.Models;

/// <summary>
/// A single queued unit of work: one source video file to downscale.
/// </summary>
/// <param name="SourcePath">Absolute path to the source video.</param>
public sealed record DownscaleRequest(string SourcePath);
