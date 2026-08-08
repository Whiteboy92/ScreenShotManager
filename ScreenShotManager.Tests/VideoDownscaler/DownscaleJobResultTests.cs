using ScreenShotManager.VideoDownscaler.Models;

namespace ScreenShotManager.Tests.VideoDownscaler;

public class DownscaleJobResultTests
{
    [Fact]
    public void Completed_populates_source_and_target()
    {
        var from = new VideoResolution(3840, 2160);
        var to = new VideoResolution(2560, 1440);
        var r = DownscaleJobResult.Completed("x.mp4", from, to);

        Assert.Equal(DownscaleOutcome.Completed, r.Outcome);
        Assert.Equal(from, r.SourceResolution);
        Assert.Equal(to, r.TargetResolution);
        Assert.Null(r.Message);
    }

    [Fact]
    public void Skipped_records_reason_and_resolution()
    {
        var res = new VideoResolution(320, 240);
        var r = DownscaleJobResult.Skipped("x.mp4", res, "too small");

        Assert.Equal(DownscaleOutcome.Skipped, r.Outcome);
        Assert.Equal(res, r.SourceResolution);
        Assert.Null(r.TargetResolution);
        Assert.Equal("too small", r.Message);
    }

    [Fact]
    public void Failed_records_error()
    {
        var r = DownscaleJobResult.Failed("x.mp4", "boom");
        Assert.Equal(DownscaleOutcome.Failed, r.Outcome);
        Assert.Equal("boom", r.Message);
        Assert.Null(r.SourceResolution);
        Assert.Null(r.TargetResolution);
    }
}
