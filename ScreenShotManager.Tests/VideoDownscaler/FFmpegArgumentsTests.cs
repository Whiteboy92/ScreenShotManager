using System;
using System.Collections.Generic;
using ScreenShotManager.VideoDownscaler.Models;
using ScreenShotManager.VideoDownscaler.Services;

namespace ScreenShotManager.Tests.VideoDownscaler;

public class FFmpegArgumentsTests
{
    private sealed class ListProgress : IProgress<double>
    {
        public List<double> Values { get; } = new();
        public void Report(double value) => Values.Add(value);
    }

    [Fact]
    public void BuildArguments_quotes_paths_and_scales_to_target()
    {
        var args = FFmpegService.BuildArguments(
            "libx264", @"C:\in dir\a.mp4", @"C:\in dir\a.downscaled.mp4", new VideoResolution(1280, 720));

        Assert.StartsWith("-y ", args);
        Assert.Contains("-i \"C:\\in dir\\a.mp4\"", args);
        Assert.Contains("scale=1280:720:force_original_aspect_ratio=decrease:force_divisible_by=2", args);
        Assert.Contains("-c:a copy", args);
        Assert.Contains("-progress pipe:1", args);
        Assert.EndsWith("\"C:\\in dir\\a.downscaled.mp4\"", args);
    }

    [Theory]
    [InlineData("h264_nvenc", "-c:v h264_nvenc")]
    [InlineData("h264_qsv", "-c:v h264_qsv")]
    [InlineData("h264_amf", "-c:v h264_amf")]
    [InlineData("libx264", "-c:v libx264 -crf 23")]
    [InlineData("unknown", "-c:v libx264 -crf 23")]
    public void BuildArguments_selects_encoder_flags(string encoder, string expected)
    {
        var args = FFmpegService.BuildArguments(encoder, "i.mp4", "o.mp4", new VideoResolution(640, 360));
        Assert.Contains(expected, args);
    }

    [Fact]
    public void ReportProgress_parses_out_time_into_fraction()
    {
        var progress = new ListProgress();
        FFmpegService.ReportProgress("out_time=00:00:05.000000", 10, progress);
        Assert.Equal(new[] { 0.5 }, progress.Values);
    }

    [Fact]
    public void ReportProgress_clamps_to_one()
    {
        var progress = new ListProgress();
        FFmpegService.ReportProgress("out_time=00:01:00.000000", 10, progress);
        Assert.Equal(new[] { 1.0 }, progress.Values);
    }

    [Theory]
    [InlineData("frame=120")]
    [InlineData("out_time_ms=5000000")]
    [InlineData("out_time=N/A")]
    [InlineData("")]
    public void ReportProgress_ignores_other_lines(string line)
    {
        var progress = new ListProgress();
        FFmpegService.ReportProgress(line, 10, progress);
        Assert.Empty(progress.Values);
    }

    [Fact]
    public void Tail_keeps_last_lines()
    {
        var text = "1\n2\n3\n4\n5\n6\n7\n8\n";
        Assert.Equal("6\n7\n8", FFmpegService.Tail(text, 3));
    }

    [Fact]
    public void Tail_returns_all_when_short()
    {
        Assert.Equal("only", FFmpegService.Tail("only\n"));
    }
}
