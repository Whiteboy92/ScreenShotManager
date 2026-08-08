using ScreenShotManager.VideoDownscaler.Models;

namespace ScreenShotManager.Tests.VideoDownscaler;

public class VideoResolutionTests
{
    [Theory]
    [InlineData(1920, 1080, 1920)]
    [InlineData(1080, 1920, 1920)]
    [InlineData(500, 500, 500)]
    public void LongestSide_picks_max_dim(int w, int h, int expected)
    {
        Assert.Equal(expected, new VideoResolution(w, h).LongestSide);
    }

    [Fact]
    public void ToString_formats_WxH()
    {
        Assert.Equal("1280x720", new VideoResolution(1280, 720).ToString());
    }

    [Fact]
    public void Equality_by_value()
    {
        Assert.Equal(new VideoResolution(1, 2), new VideoResolution(1, 2));
        Assert.NotEqual(new VideoResolution(1, 2), new VideoResolution(2, 1));
    }
}
