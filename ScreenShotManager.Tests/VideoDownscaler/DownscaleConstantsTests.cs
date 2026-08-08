using ScreenShotManager.VideoDownscaler.Models;

namespace ScreenShotManager.Tests.VideoDownscaler;

public class DownscaleConstantsTests
{
    [Theory]
    [InlineData("movie.mp4", true)]
    [InlineData("movie.MP4", true)]
    [InlineData("clip.mkv", true)]
    [InlineData("a/b/c.mov", true)]
    [InlineData("x.webm", true)]
    [InlineData("x.avi", true)]
    [InlineData("x.m4v", true)]
    [InlineData("x.ts", true)]
    [InlineData("photo.png", false)]
    [InlineData("noext", false)]
    [InlineData("", false)]
    public void IsSupported_matches_video_extensions(string path, bool expected)
    {
        Assert.Equal(expected, DownscaleConstants.IsSupported(path));
    }

    [Fact]
    public void Constants_expose_expected_identifiers()
    {
        Assert.Equal("--downscale", DownscaleConstants.CliArgument);
        Assert.False(string.IsNullOrEmpty(DownscaleConstants.PipeName));
        Assert.False(string.IsNullOrEmpty(DownscaleConstants.SingleInstanceMutexName));
        Assert.False(string.IsNullOrEmpty(DownscaleConstants.ContextMenuVerb));
    }
}
