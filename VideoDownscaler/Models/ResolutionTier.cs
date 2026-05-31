using System.Collections.Generic;

namespace ScreenShotManager.VideoDownscaler.Models;

/// <summary>
/// Standard 16:9 resolution ladder and the logic that maps an arbitrary source
/// resolution to the next lower tier.
/// </summary>
public static class ResolutionTier
{
    /// <summary>Resolution ladder, descending.</summary>
    public static readonly IReadOnlyList<VideoResolution> Tiers = new[]
    {
        new VideoResolution(7680, 4320),
        new VideoResolution(3840, 2160),
        new VideoResolution(2560, 1440),
        new VideoResolution(1920, 1080),
        new VideoResolution(1280, 720),
        new VideoResolution(854, 480),
        new VideoResolution(640, 360),
        new VideoResolution(426, 240),
    };

    /// <summary>Lowest tier on the ladder; sources already at/below it are skipped.</summary>
    public static VideoResolution Floor => Tiers[Tiers.Count - 1];

    /// <summary>
    /// Returns the highest tier strictly smaller than <paramref name="source"/>, ranked by the
    /// longest side. Returns <c>null</c> when the source is already at or below the lowest tier.
    /// </summary>
    /// <example>3840x2160 → 2560x1440; 2104x1200 → 1920x1080; 426x240 → null.</example>
    public static VideoResolution? NextLowerTier(VideoResolution source)
    {
        var sourceMax = source.LongestSide;
        foreach (var tier in Tiers)
        {
            // Tiers are descending; first tier whose longest side is strictly below the source wins.
            if (tier.LongestSide < sourceMax)
            {
                return tier;
            }
        }

        return null;
    }
}
