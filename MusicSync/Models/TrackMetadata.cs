namespace ScreenShotManager.MusicSync.Models;

/// <summary>Tag data read from an audio file. Every field is optional; untagged files fall back to the filename.</summary>
public sealed record TrackMetadata(
    string? Title,
    string? Artist,
    string? Album,
    uint? TrackNumber,
    long? DurationMs,
    bool HasArt,
    bool HasEmbeddedLyrics = false)
{
    public static TrackMetadata None { get; } = new(null, null, null, null, null, false);
}

/// <summary>Embedded cover image bytes plus their MIME type.</summary>
public sealed record AlbumArt(byte[] Data, string ContentType);
