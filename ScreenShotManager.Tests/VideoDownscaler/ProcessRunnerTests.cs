using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ScreenShotManager.VideoDownscaler.Services;

namespace ScreenShotManager.Tests.VideoDownscaler;

public class ProcessRunnerTests
{
    private static readonly string Cmd = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";

    [Fact]
    public async Task Captures_stdout_and_success()
    {
        var result = await ProcessRunner.RunAsync(Cmd, "/c echo hello", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("hello", result.StdOut.Trim());
    }

    [Fact]
    public async Task Captures_stderr_and_exit_code()
    {
        var result = await ProcessRunner.RunAsync(Cmd, "/c \"echo oops 1>&2 & exit 3\"", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains("oops", result.StdErr);
    }

    [Fact]
    public async Task Invokes_line_callback_per_stdout_line()
    {
        var lines = new List<string>();
        await ProcessRunner.RunAsync(Cmd, "/c \"echo one& echo two\"", CancellationToken.None,
            line => { lock (lines) lines.Add(line.Trim()); });

        Assert.Equal(new[] { "one", "two" }, lines);
    }

    [Fact]
    public async Task Cancellation_kills_process_and_throws()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var sw = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ProcessRunner.RunAsync(Cmd, "/c ping -n 30 127.0.0.1", cts.Token));

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Missing_executable_throws()
    {
        await Assert.ThrowsAsync<Win32Exception>(() =>
            ProcessRunner.RunAsync(@"C:\definitely\not\here.exe", "", CancellationToken.None));
    }

    [Fact]
    public void ProcessResult_success_only_on_zero()
    {
        Assert.True(new ProcessResult(0, "", "").Success);
        Assert.False(new ProcessResult(1, "", "").Success);
        Assert.False(new ProcessResult(-1, "", "").Success);
    }
}
