using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using ScreenShotManager.MusicSync.Models;

namespace ScreenShotManager.MusicSync.Services.Interfaces;

/// <summary>
/// Persists per-file hash + tags keyed by path, size and modified time, so a rescan only re-reads
/// files that actually changed. Hashing a large library from scratch can take minutes.
/// </summary>
public interface ITrackCache
{
    bool TryGet(string fullPath, long sizeBytes, DateTime modifiedUtc, [NotNullWhen(true)] out CachedTrack? entry);

    void Set(string fullPath, CachedTrack entry);

    /// <summary>Drops entries for files no longer in the library.</summary>
    void Retain(IReadOnlyCollection<string> livePaths);

    void Save();
}

public sealed record CachedTrack(
    long SizeBytes,
    long ModifiedUtcTicks,
    string Sha256,
    TrackMetadata Metadata);
