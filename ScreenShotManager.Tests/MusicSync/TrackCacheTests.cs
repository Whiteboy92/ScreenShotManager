using System;
using System.IO;
using ScreenShotManager.MusicSync.Models;
using ScreenShotManager.MusicSync.Services;
using ScreenShotManager.MusicSync.Services.Interfaces;

namespace ScreenShotManager.Tests.MusicSync;

public class TrackCacheTests
{
    private static readonly DateTime Modified = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private static CachedTrack Entry(string sha = "abc") =>
        new(100, Modified.Ticks, sha, new TrackMetadata("T", "A", null, 1, 2000, false));

    [Fact]
    public void Persists_across_instances()
    {
        using var temp = new TempFolder();
        var cachePath = Path.Combine(temp.Path, "cache.json");

        var first = new TrackCache(cachePath);
        first.Set(@"C:\Music\a.mp3", Entry());
        first.Save();

        var second = new TrackCache(cachePath);
        Assert.True(second.TryGet(@"c:\music\A.MP3", 100, Modified, out var loaded));
        Assert.Equal("abc", loaded.Sha256);
        Assert.Equal("T", loaded.Metadata.Title);
        Assert.Equal(2000L, loaded.Metadata.DurationMs);
    }

    [Fact]
    public void Misses_when_size_or_time_differs()
    {
        using var temp = new TempFolder();
        var cache = new TrackCache(Path.Combine(temp.Path, "cache.json"));
        cache.Set("a", Entry());

        Assert.False(cache.TryGet("a", 101, Modified, out _));
        Assert.False(cache.TryGet("a", 100, Modified.AddSeconds(1), out _));
        Assert.False(cache.TryGet("b", 100, Modified, out _));
    }

    [Fact]
    public void Retain_drops_dead_paths()
    {
        using var temp = new TempFolder();
        var cache = new TrackCache(Path.Combine(temp.Path, "cache.json"));
        cache.Set("keep", Entry());
        cache.Set("drop", Entry());

        cache.Retain(new[] { "keep" });

        Assert.True(cache.TryGet("keep", 100, Modified, out _));
        Assert.False(cache.TryGet("drop", 100, Modified, out _));
    }

    [Fact]
    public void Corrupt_file_starts_empty()
    {
        using var temp = new TempFolder();
        var cachePath = temp.Write("cache.json", "{ not json"u8.ToArray());

        var cache = new TrackCache(cachePath);

        Assert.False(cache.TryGet("a", 100, Modified, out _));
    }
}
