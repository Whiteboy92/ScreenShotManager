using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using ScreenShotManager.MusicSync.Models;
using ScreenShotManager.MusicSync.Services.Interfaces;

namespace ScreenShotManager.Tests.MusicSync;

/// <summary>Counts reads so tests can prove the cache skips unchanged files.</summary>
internal sealed class FakeMetadataReader : ITrackMetadataReader
{
    private int readCount;

    public int ReadCount => readCount;

    public Func<string, TrackMetadata> Metadata { get; set; } = _ => TrackMetadata.None;

    public AlbumArt? Art { get; set; }

    public TrackMetadata Read(string path)
    {
        Interlocked.Increment(ref readCount);
        return Metadata(path);
    }

    public AlbumArt? ReadArt(string path) => Art;

    public string? Lyrics { get; set; }

    public string? ReadLyrics(string path) => Lyrics;
}

/** Returns a fixed analysis and counts calls; no ffmpeg needed. */
internal sealed class FakeAnalyzer : ITrackAnalyzer
{
    private int calls;

    public int Calls => calls;

    public bool IsAvailable => true;

    public System.Threading.Tasks.Task<TrackAnalysis?> AnalyzeAsync(string path, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        return System.Threading.Tasks.Task.FromResult<TrackAnalysis?>(
            new TrackAnalysis(-12, -1, new byte[MusicSyncConstants.WaveformPoints]));
    }
}

internal sealed class InMemoryTrackCache : ITrackCache
{
    private readonly ConcurrentDictionary<string, CachedTrack> entries = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string fullPath, long sizeBytes, DateTime modifiedUtc, [NotNullWhen(true)] out CachedTrack? entry)
    {
        if (entries.TryGetValue(fullPath, out var cached)
            && cached.SizeBytes == sizeBytes
            && cached.ModifiedUtcTicks == modifiedUtc.Ticks)
        {
            entry = cached;
            return true;
        }

        entry = null;
        return false;
    }

    public void Set(string fullPath, CachedTrack entry) => entries[fullPath] = entry;

    public void Retain(IReadOnlyCollection<string> livePaths)
    {
        var live = new HashSet<string>(livePaths, StringComparer.OrdinalIgnoreCase);
        foreach (var key in entries.Keys)
        {
            if (!live.Contains(key))
            {
                entries.TryRemove(key, out _);
            }
        }
    }

    public void Save()
    {
    }
}

/// <summary>Temp directory deleted on dispose.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ssm-music-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Write(string relativePath, byte[] content)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best-effort */ }
    }
}
