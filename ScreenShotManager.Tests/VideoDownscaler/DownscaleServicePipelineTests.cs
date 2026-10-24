using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ScreenShotManager.VideoDownscaler.Models;
using ScreenShotManager.VideoDownscaler.Services;
using ScreenShotManager.VideoDownscaler.Services.Interfaces;

namespace ScreenShotManager.Tests.VideoDownscaler;

public class DownscaleServicePipelineTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly string dir;

    public DownscaleServicePipelineTests()
    {
        dir = Path.Combine(Path.GetTempPath(), "ssm-pipe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    public void Dispose() => Directory.Delete(dir, true);

    private sealed class StubProbe : IFFprobeService
    {
        public Func<string, VideoResolution> Resolution { get; set; } = _ => new VideoResolution(1920, 1080);
        public Task<VideoResolution> GetResolutionAsync(string filePath, CancellationToken ct = default) =>
            Task.FromResult(Resolution(filePath));
        public Task<TimeSpan> GetDurationAsync(string filePath, CancellationToken ct = default) =>
            Task.FromResult(TimeSpan.FromSeconds(10));
    }

    private sealed class StubEncoder : IFFmpegService
    {
        public ConcurrentQueue<(string Input, string Output, VideoResolution Target)> Calls { get; } = new();
        public Func<CancellationToken, Task> Body { get; set; } = _ => Task.CompletedTask;

        public async Task DownscaleAsync(string i, string o, VideoResolution t, TimeSpan d,
            IProgress<double>? p = null, CancellationToken ct = default)
        {
            Calls.Enqueue((i, o, t));
            p?.Report(0.5);
            await Body(ct);
        }
    }

    private sealed class StubTools : IToolPathProvider
    {
        public string FFmpegPath => "ffmpeg";
        public string FFprobePath => "ffprobe";
        public bool ToolsAvailable => true;
    }

    private string NewVideo(string name)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    private static Task<DownscaleJobResult> NextResult(DownscaleService svc)
    {
        var tcs = new TaskCompletionSource<DownscaleJobResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<DownscaleJobResult>? handler = null;
        handler = (_, r) =>
        {
            svc.JobFinished -= handler;
            tcs.TrySetResult(r);
        };
        svc.JobFinished += handler;
        return tcs.Task;
    }

    [Fact]
    public async Task Completes_job_with_next_lower_tier_and_sibling_output()
    {
        var encoder = new StubEncoder();
        using var svc = new DownscaleService(new StubProbe(), encoder, new StubTools());
        var started = new TaskCompletionSource<DownscaleRequest>();
        svc.JobStarted += (_, r) => started.TrySetResult(r);
        var finished = NextResult(svc);
        var src = NewVideo("movie.mp4");

        svc.Start();
        Assert.True(svc.Enqueue(src));

        var result = await finished.WaitAsync(Timeout);
        Assert.Equal(DownscaleOutcome.Completed, result.Outcome);
        Assert.Equal(new VideoResolution(1920, 1080), result.SourceResolution);
        Assert.Equal(new VideoResolution(1280, 720), result.TargetResolution);
        Assert.Equal(src, (await started.Task.WaitAsync(Timeout)).SourcePath);

        Assert.True(encoder.Calls.TryDequeue(out var call));
        Assert.Equal(src, call.Input);
        Assert.Equal(Path.Combine(dir, "movie.downscaled.mp4"), call.Output);
        Assert.Equal(new VideoResolution(1280, 720), call.Target);
    }

    [Fact]
    public async Task Raises_progress_from_encoder()
    {
        using var svc = new DownscaleService(new StubProbe(), new StubEncoder(), new StubTools());
        var progress = new TaskCompletionSource<DownscaleProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.JobProgress += (_, p) => progress.TrySetResult(p);
        var src = NewVideo("p.mp4");

        svc.Start();
        svc.Enqueue(src);

        var p = await progress.Task.WaitAsync(Timeout);
        Assert.Equal(src, p.SourcePath);
        Assert.Equal(0.5, p.Fraction);
    }

    [Fact]
    public async Task Skips_when_source_at_floor()
    {
        var encoder = new StubEncoder();
        var probe = new StubProbe { Resolution = _ => ResolutionTier.Floor };
        using var svc = new DownscaleService(probe, encoder, new StubTools());
        var finished = NextResult(svc);

        svc.Start();
        svc.Enqueue(NewVideo("tiny.mp4"));

        var result = await finished.WaitAsync(Timeout);
        Assert.Equal(DownscaleOutcome.Skipped, result.Outcome);
        Assert.Equal(ResolutionTier.Floor, result.SourceResolution);
        Assert.Empty(encoder.Calls);
    }

    [Fact]
    public async Task Probe_failure_yields_failed_result()
    {
        var probe = new StubProbe { Resolution = _ => throw new InvalidOperationException("boom") };
        using var svc = new DownscaleService(probe, new StubEncoder(), new StubTools());
        var finished = NextResult(svc);

        svc.Start();
        svc.Enqueue(NewVideo("bad.mp4"));

        var result = await finished.WaitAsync(Timeout);
        Assert.Equal(DownscaleOutcome.Failed, result.Outcome);
        Assert.Equal("boom", result.Message);
    }

    [Fact]
    public async Task Encoder_failure_does_not_stop_queue()
    {
        var encoder = new StubEncoder();
        var calls = 0;
        encoder.Body = _ => Interlocked.Increment(ref calls) == 1
            ? Task.FromException(new InvalidOperationException("enc"))
            : Task.CompletedTask;
        using var svc = new DownscaleService(new StubProbe(), encoder, new StubTools());

        var results = new BlockingCollection<DownscaleJobResult>();
        svc.JobFinished += (_, r) => results.Add(r);

        svc.Start();
        svc.Enqueue(NewVideo("a.mp4"));
        svc.Enqueue(NewVideo("b.mp4"));

        Assert.True(results.TryTake(out var first, Timeout));
        Assert.True(results.TryTake(out var second, Timeout));
        Assert.Equal(DownscaleOutcome.Failed, first!.Outcome);
        Assert.Equal("enc", first.Message);
        Assert.Equal(DownscaleOutcome.Completed, second!.Outcome);
    }

    [Fact]
    public async Task Processes_jobs_sequentially_in_enqueue_order()
    {
        var encoder = new StubEncoder();
        var inFlight = 0;
        var maxInFlight = 0;
        encoder.Body = async _ =>
        {
            var now = Interlocked.Increment(ref inFlight);
            InterlockedMax(ref maxInFlight, now);
            await Task.Delay(20);
            Interlocked.Decrement(ref inFlight);
        };
        using var svc = new DownscaleService(new StubProbe(), encoder, new StubTools());

        var results = new BlockingCollection<DownscaleJobResult>();
        svc.JobFinished += (_, r) => results.Add(r);

        var paths = new List<string> { NewVideo("1.mp4"), NewVideo("2.mp4"), NewVideo("3.mp4") };
        foreach (var p in paths)
        {
            svc.Enqueue(p);
        }

        svc.Start();

        foreach (var expected in paths)
        {
            Assert.True(results.TryTake(out var r, Timeout));
            Assert.Equal(expected, r!.SourcePath);
        }

        Assert.Equal(1, maxInFlight);
    }

    [Fact]
    public async Task Stop_cancels_running_job()
    {
        var encoder = new StubEncoder { Body = ct => Task.Delay(System.Threading.Timeout.Infinite, ct) };
        using var svc = new DownscaleService(new StubProbe(), encoder, new StubTools());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.JobStarted += (_, _) => started.TrySetResult();
        var finished = NextResult(svc);

        svc.Start();
        svc.Enqueue(NewVideo("long.mp4"));
        await started.Task.WaitAsync(Timeout);
        svc.Stop();

        var result = await finished.WaitAsync(Timeout);
        Assert.Equal(DownscaleOutcome.Failed, result.Outcome);
        Assert.Equal("Cancelled.", result.Message);
    }

    [Fact]
    public async Task Throwing_event_handler_does_not_break_worker()
    {
        using var svc = new DownscaleService(new StubProbe(), new StubEncoder(), new StubTools());
        svc.JobStarted += (_, _) => throw new InvalidOperationException("handler");

        var results = new BlockingCollection<DownscaleJobResult>();
        svc.JobFinished += (_, r) => results.Add(r);

        svc.Start();
        svc.Enqueue(NewVideo("a.mp4"));
        svc.Enqueue(NewVideo("b.mp4"));

        Assert.True(results.TryTake(out var a, Timeout));
        Assert.True(results.TryTake(out var b, Timeout));
        Assert.Equal(DownscaleOutcome.Completed, a!.Outcome);
        Assert.Equal(DownscaleOutcome.Completed, b!.Outcome);
    }

    [Fact]
    public void Enqueue_normalizes_to_full_path()
    {
        var src = NewVideo("rel.mp4");
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, src);
        using var svc = new DownscaleService(new StubProbe(), new StubEncoder(), new StubTools());
        DownscaleRequest? seen = null;
        var done = new ManualResetEventSlim();
        svc.JobStarted += (_, r) => { seen = r; done.Set(); };

        Assert.True(svc.Enqueue(relative));
        svc.Start();

        Assert.True(done.Wait(Timeout));
        Assert.Equal(src, seen!.SourcePath);
    }

    [Fact]
    public void Enqueue_after_stop_is_rejected()
    {
        using var svc = new DownscaleService(new StubProbe(), new StubEncoder(), new StubTools());
        svc.Stop();
        Assert.False(svc.Enqueue(NewVideo("late.mp4")));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)) &&
               Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
