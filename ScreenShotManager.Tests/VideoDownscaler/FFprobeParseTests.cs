using System;
using System.Text.Json;
using ScreenShotManager.VideoDownscaler.Models;
using ScreenShotManager.VideoDownscaler.Services;

namespace ScreenShotManager.Tests.VideoDownscaler;

public class FFprobeParseTests
{
    [Fact]
    public void Parse_reads_numeric_dimensions()
    {
        var json = """{"streams":[{"width":1920,"height":1080}]}""";
        Assert.Equal(new VideoResolution(1920, 1080), FFprobeService.Parse(json, "a.mp4"));
    }

    [Fact]
    public void Parse_reads_string_dimensions()
    {
        var json = """{"streams":[{"width":"3840","height":"2160"}]}""";
        Assert.Equal(new VideoResolution(3840, 2160), FFprobeService.Parse(json, "a.mp4"));
    }

    [Fact]
    public void Parse_uses_first_stream_only()
    {
        var json = """{"streams":[{"width":640,"height":360},{"width":1920,"height":1080}]}""";
        Assert.Equal(new VideoResolution(640, 360), FFprobeService.Parse(json, "a.mp4"));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"streams":[]}""")]
    public void Parse_throws_when_no_video_stream(string json)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => FFprobeService.Parse(json, "a.mp4"));
        Assert.Contains("No video stream", ex.Message);
    }

    [Theory]
    [InlineData("""{"streams":[{"width":0,"height":1080}]}""")]
    [InlineData("""{"streams":[{"width":1920}]}""")]
    [InlineData("""{"streams":[{"width":"abc","height":1080}]}""")]
    [InlineData("""{"streams":[{"width":-5,"height":1080}]}""")]
    public void Parse_throws_on_invalid_dimensions(string json)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => FFprobeService.Parse(json, "a.mp4"));
        Assert.Contains("Invalid dimensions", ex.Message);
    }

    [Fact]
    public void Parse_wraps_malformed_json()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => FFprobeService.Parse("not json", "a.mp4"));
        Assert.IsAssignableFrom<JsonException>(ex.InnerException);
        Assert.Contains("a.mp4", ex.Message);
    }

    [Fact]
    public void ParseDuration_reads_seconds_string()
    {
        var json = """{"format":{"duration":"12.500000"}}""";
        Assert.Equal(TimeSpan.FromSeconds(12.5), FFprobeService.ParseDuration(json));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"format":{}}""")]
    [InlineData("""{"format":{"duration":"N/A"}}""")]
    [InlineData("""{"format":{"duration":"0"}}""")]
    [InlineData("""{"format":{"duration":"-3"}}""")]
    [InlineData("garbage")]
    public void ParseDuration_returns_zero_when_unknown(string json)
    {
        Assert.Equal(TimeSpan.Zero, FFprobeService.ParseDuration(json));
    }
}
