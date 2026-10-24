using System;
using System.Text.Json.Serialization;

namespace ScreenShotManager.MusicSync.Models;

/// <summary>
/// One song as published in the manifest. <see cref="Id"/> is derived from the relative path, so it
/// stays stable across rescans; <see cref="Sha256"/> changes when the file's content does.
/// </summary>
public sealed record TrackInfo
{
    public required string Id { get; init; }

    /// <summary>Path relative to the music folder, '/'-separated (sub-folders kept for the phone).</summary>
    public required string RelativePath { get; init; }

    public required long SizeBytes { get; init; }

    /// <summary>When the file appeared in the folder (Windows creation time). "Latest" sorts by this.</summary>
    public required DateTimeOffset AddedUtc { get; init; }

    public required DateTimeOffset ModifiedUtc { get; init; }

    public required string Sha256 { get; init; }

    public required string ContentType { get; init; }

    public string? Title { get; init; }

    public string? Artist { get; init; }

    public string? Album { get; init; }

    public uint? TrackNumber { get; init; }

    public long? DurationMs { get; init; }

    public bool HasArt { get; init; }

    /// <summary>Absolute path on this PC. Never sent to the phone.</summary>
    [JsonIgnore]
    public string FullPath { get; init; } = string.Empty;
}
