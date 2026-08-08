using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ScreenShotManager.VideoDownscaler.Models;
using ScreenShotManager.VideoDownscaler.Services;
using ScreenShotManager.VideoDownscaler.Services.Interfaces;

namespace ScreenShotManager.Tests.VideoDownscaler;

public class DownscaleServiceEnqueueTests
{
    private sealed class StubProbe : IFFprobeService
    {
        public Task<VideoResolution> GetResolutionAsync(string filePath, CancellationToken ct = default) =>
            Task.FromResult(new VideoResolution(1920, 1080));
        public Task<TimeSpan> GetDurationAsync(string filePath, CancellationToken ct = default) =>
            Task.FromResult(TimeSpan.Zero);
    }

    private sealed class StubEncoder : IFFmpegService
    {
        public Task DownscaleAsync(string i, string o, VideoResolution t, TimeSpan d,
            IProgress<double>? p = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubTools : IToolPathProvider
    {
        public string FFmpegPath => "ffmpeg";
        public string FFprobePath => "ffprobe";
        public bool ToolsAvailable => true;
    }

    private static DownscaleService NewService() => new(new StubProbe(), new StubEncoder(), new StubTools());

    [Fact]
    public void Enqueue_rejects_null_or_whitespace()
    {
        using var svc = NewService();
        Assert.False(svc.Enqueue(""));
        Assert.False(svc.Enqueue("   "));
        Assert.False(svc.Enqueue(null!));
    }

    [Fact]
    public void Enqueue_rejects_unsupported_extension()
    {
        using var svc = NewService();
        var path = Path.Combine(Path.GetTempPath(), "img.png");
        File.WriteAllText(path, "x");
        try { Assert.False(svc.Enqueue(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Enqueue_rejects_missing_file()
    {
        using var svc = NewService();
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp4");
        Assert.False(svc.Enqueue(missing));
    }

    [Fact]
    public void Enqueue_accepts_supported_existing_file()
    {
        using var svc = NewService();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp4");
        File.WriteAllText(path, "x");
        try { Assert.True(svc.Enqueue(path)); }
        finally { File.Delete(path); }
    }
}
