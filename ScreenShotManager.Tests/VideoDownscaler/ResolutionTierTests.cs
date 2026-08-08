using ScreenShotManager.VideoDownscaler.Models;

namespace ScreenShotManager.Tests.VideoDownscaler;

public class ResolutionTierTests
{
    [Fact]
    public void Floor_is_lowest_tier()
    {
        Assert.Equal(new VideoResolution(426, 240), ResolutionTier.Floor);
    }

    [Fact]
    public void Tiers_are_strictly_descending_by_longest_side()
    {
        var tiers = ResolutionTier.Tiers;
        for (var i = 1; i < tiers.Count; i++)
        {
            Assert.True(tiers[i - 1].LongestSide > tiers[i].LongestSide);
        }
    }

    [Theory]
    [InlineData(3840, 2160, 2560, 1440)]
    [InlineData(2560, 1440, 1920, 1080)]
    [InlineData(1920, 1080, 1280, 720)]
    [InlineData(2104, 1200, 1920, 1080)]
    [InlineData(7680, 4320, 3840, 2160)]
    public void NextLowerTier_returns_next_tier_by_longest_side(int w, int h, int expW, int expH)
    {
        var next = ResolutionTier.NextLowerTier(new VideoResolution(w, h));
        Assert.NotNull(next);
        Assert.Equal(new VideoResolution(expW, expH), next);
    }

    [Theory]
    [InlineData(426, 240)]
    [InlineData(320, 240)]
    [InlineData(100, 100)]
    public void NextLowerTier_returns_null_at_or_below_floor(int w, int h)
    {
        Assert.Null(ResolutionTier.NextLowerTier(new VideoResolution(w, h)));
    }

    [Fact]
    public void NextLowerTier_handles_portrait()
    {
        var next = ResolutionTier.NextLowerTier(new VideoResolution(1080, 1920));
        Assert.Equal(new VideoResolution(1280, 720), next);
    }
}
