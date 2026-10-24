using System;
using System.Net;
using ScreenShotManager.MusicSync.Models;
using ScreenShotManager.MusicSync.Services;

namespace ScreenShotManager.Tests.MusicSync;

public class NetworkGuardTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("192.168.0.61", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("::ffff:192.168.0.61", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fd12:3456::1", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("100.64.0.1", false)] // CGNAT / Tailscale
    [InlineData("169.254.1.1", false)]
    [InlineData("2001:4860::8888", false)]
    [InlineData("::ffff:8.8.8.8", false)]
    public void Only_loopback_and_private_networks_are_local(string address, bool expected) =>
        Assert.Equal(expected, NetworkGuard.IsLocalNetwork(IPAddress.Parse(address)));

    [Fact]
    public void Null_address_is_not_local() => Assert.False(NetworkGuard.IsLocalNetwork(null));

    [Fact]
    public void Limiter_locks_after_max_failures_within_window()
    {
        var clock = new ManualClock();
        var limiter = new AuthFailureLimiter(clock);
        var phone = IPAddress.Parse("192.168.0.61");

        for (var i = 1; i < MusicSyncConstants.MaxAuthFailures; i++)
        {
            Assert.False(limiter.RecordFailure(phone));
        }

        Assert.Null(limiter.LockedFor(phone));
        Assert.True(limiter.RecordFailure(phone));
        Assert.Equal(MusicSyncConstants.AuthLockout, limiter.LockedFor(phone));
        Assert.Null(limiter.LockedFor(IPAddress.Parse("192.168.0.62")));

        clock.Advance(MusicSyncConstants.AuthLockout);
        Assert.Null(limiter.LockedFor(phone));
    }

    [Fact]
    public void Failures_spread_beyond_the_window_never_lock()
    {
        var clock = new ManualClock();
        var limiter = new AuthFailureLimiter(clock);
        var phone = IPAddress.Parse("::ffff:192.168.0.61");

        for (var i = 0; i < MusicSyncConstants.MaxAuthFailures * 3; i++)
        {
            Assert.False(limiter.RecordFailure(phone));
            clock.Advance(MusicSyncConstants.AuthFailureWindow / (MusicSyncConstants.MaxAuthFailures - 1) + TimeSpan.FromSeconds(1));
        }

        Assert.Null(limiter.LockedFor(IPAddress.Parse("192.168.0.61")));
    }
}

/// <summary>Settable clock for lockout tests.</summary>
public sealed class ManualClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}
