using System.IO;
using ScreenShotManager.VideoDownscaler.Services;

namespace ScreenShotManager.Tests.VideoDownscaler;

public class ToolPathProviderTests
{
    [Fact]
    public void Paths_end_in_expected_exe_names()
    {
        var p = new ToolPathProvider();
        Assert.EndsWith("ffmpeg.exe", p.FFmpegPath);
        Assert.EndsWith("ffprobe.exe", p.FFprobePath);
    }

    [Fact]
    public void ToolsAvailable_matches_file_existence()
    {
        var p = new ToolPathProvider();
        var expected = File.Exists(p.FFmpegPath) && File.Exists(p.FFprobePath);
        Assert.Equal(expected, p.ToolsAvailable);
    }
}
