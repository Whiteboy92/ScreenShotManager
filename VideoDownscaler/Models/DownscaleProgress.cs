namespace ScreenShotManager.VideoDownscaler.Models;

/// <summary>
/// Progress update for an in-flight downscale job. <see cref="Fraction"/> is 0..1.
/// </summary>
public readonly record struct DownscaleProgress(string SourcePath, double Fraction);
