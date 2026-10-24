using ScreenShotManager.MusicSync.Models;

namespace ScreenShotManager.MusicSync.Services.Interfaces;

/// <summary>Reads tags and cover art from audio files. Never throws for bad/untagged files.</summary>
public interface ITrackMetadataReader
{
    TrackMetadata Read(string path);

    /// <summary>Embedded front cover, else a cover.jpg/folder.jpg next to the file, else null.</summary>
    AlbumArt? ReadArt(string path);
}
