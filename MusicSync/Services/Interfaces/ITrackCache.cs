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

    /// <summary>Last entry for this path even if the file changed since (to keep results that still apply).</summary>
    bool TryGetPrevious(string fullPath, [NotNullWhen(true)] out CachedTrack? entry)
    {
        entry = null;
        return false;
    }

    /// <summary>Drops entries for files no longer in the library.</summary>
    void Retain(IReadOnlyCollection<string> livePaths);

    void Save();
}

public sealed record CachedTrack(
    long SizeBytes,
    long ModifiedUtcTicks,
    string Sha256,
    TrackMetadata Metadata)
{
    /// <summary>Filled in later by the background analyzer; null until then.</summary>
    public TrackAnalysis? Analysis { get; init; }

    /// <summary>SHA-256 of the audio without its tags (MP3 only, else null); survives tag edits.</summary>
    public string? AudioSha256 { get; init; }

    /// <summary>Tag-reader version that produced <see cref="Metadata"/> (see MusicSyncConstants.MetadataVersion).</summary>
    public int MetadataVersion { get; init; }
}
