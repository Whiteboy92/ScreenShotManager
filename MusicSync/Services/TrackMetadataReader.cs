using System;
using System.IO;
using System.Linq;
using ScreenShotManager.MusicSync.Models;
using ScreenShotManager.MusicSync.Services.Interfaces;

namespace ScreenShotManager.MusicSync.Services;

/// <summary>
/// TagLib#-backed tag reader. Corrupt or untagged files yield <see cref="TrackMetadata.None"/>
/// rather than failing the whole scan.
/// </summary>
public sealed class TrackMetadataReader : ITrackMetadataReader
{
    /// <summary>Loose cover files many rippers drop next to the tracks, checked in order.</summary>
    private static readonly string[] FolderArtNames =
    {
        "cover.jpg", "cover.png", "folder.jpg", "folder.png", "front.jpg", "front.png", "albumart.jpg",
    };

    public TrackMetadata Read(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var tag = file.Tag;
            var duration = file.Properties?.Duration ?? TimeSpan.Zero;

            return new TrackMetadata(
                NullIfBlank(tag.Title),
                NullIfBlank(tag.FirstPerformer) ?? NullIfBlank(tag.FirstAlbumArtist),
                NullIfBlank(tag.Album),
                tag.Track == 0 ? null : tag.Track,
                duration > TimeSpan.Zero ? (long)duration.TotalMilliseconds : null,
                tag.Pictures.Length > 0 || FindFolderArt(path) != null);
        }
        catch
        {
            return TrackMetadata.None with { HasArt = FindFolderArt(path) != null };
        }
    }

    public AlbumArt? ReadArt(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var pictures = file.Tag.Pictures;
            var picture = pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover)
                          ?? pictures.FirstOrDefault();

            if (picture?.Data?.Data is { Length: > 0 } data)
            {
                return new AlbumArt(data, string.IsNullOrWhiteSpace(picture.MimeType) ? "image/jpeg" : picture.MimeType);
            }
        }
        catch
        {
            // Fall through to folder art.
        }

        var folderArt = FindFolderArt(path);
        if (folderArt == null)
        {
            return null;
        }

        try
        {
            var contentType = folderArt.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
            return new AlbumArt(File.ReadAllBytes(folderArt), contentType);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindFolderArt(string trackPath)
    {
        var directory = Path.GetDirectoryName(trackPath);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        return FolderArtNames
            .Select(name => Path.Combine(directory, name))
            .FirstOrDefault(File.Exists);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
