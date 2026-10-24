using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ScreenShotManager.VideoDownscaler.Services;

namespace ScreenShotManager.Tests.VideoDownscaler;

public class DownscaleIpcTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    // Unique per test so a running app instance (real pipe name) is never touched.
    private static string NewPipeName() => "ssm-test-pipe-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Client_paths_reach_server_skipping_blank_lines()
    {
        var pipe = NewPipeName();
        using var server = new DownscaleIpcServer(pipe);
        var received = new BlockingCollection<string>();
        server.PathReceived += (_, p) => received.Add(p);
        server.Start();

        var sent = await new DownscaleIpcClient(pipe).TrySendAsync(new[] { @"C:\a.mp4", "", "   ", @"C:\b.mkv" });

        Assert.True(sent);
        Assert.True(received.TryTake(out var first, Timeout));
        Assert.True(received.TryTake(out var second, Timeout));
        Assert.Equal(@"C:\a.mp4", first);
        Assert.Equal(@"C:\b.mkv", second);
        Assert.False(received.TryTake(out _, TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task Server_accepts_successive_connections()
    {
        var pipe = NewPipeName();
        using var server = new DownscaleIpcServer(pipe);
        var received = new BlockingCollection<string>();
        server.PathReceived += (_, p) => received.Add(p);
        server.Start();

        var client = new DownscaleIpcClient(pipe);
        Assert.True(await client.TrySendAsync(new[] { "one" }));
        Assert.True(received.TryTake(out var a, Timeout));
        Assert.True(await client.TrySendAsync(new[] { "two" }));
        Assert.True(received.TryTake(out var b, Timeout));

        Assert.Equal("one", a);
        Assert.Equal("two", b);
    }

    [Fact]
    public async Task Throwing_handler_does_not_stop_server()
    {
        var pipe = NewPipeName();
        using var server = new DownscaleIpcServer(pipe);
        var received = new BlockingCollection<string>();
        server.PathReceived += (_, p) =>
        {
            received.Add(p);
            throw new InvalidOperationException("handler");
        };
        server.Start();

        await new DownscaleIpcClient(pipe).TrySendAsync(new[] { "x", "y" });

        Assert.True(received.TryTake(out _, Timeout));
        Assert.True(received.TryTake(out _, Timeout));
    }

    [Fact]
    public async Task Client_returns_false_when_no_server()
    {
        var sent = await new DownscaleIpcClient(NewPipeName()).TrySendAsync(new[] { "x" });
        Assert.False(sent);
    }

    [Fact]
    public async Task Client_returns_false_when_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sent = await new DownscaleIpcClient(NewPipeName()).TrySendAsync(new[] { "x" }, cts.Token);
        Assert.False(sent);
    }

    [Fact]
    public void Server_dispose_without_start_is_safe()
    {
        var server = new DownscaleIpcServer(NewPipeName());
        server.Dispose();
    }
}
