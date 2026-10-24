using System;
using System.IO;

namespace ScreenShotManager.Screenshot.Models;

/// <summary>
/// Application settings model
/// </summary>
public class AppSettings
{
    public string SaveFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "Screenshots"
    );

    public string FileFormat { get; set; } = "PNG";
    public string FilenamePattern { get; set; } = "Screenshot_{date}_{time}";
    public bool DisableDimming { get; set; }
    public bool StartOnLogin { get; set; }
    public int MaxAutoSaveHistory { get; set; } = 100;

    // Music Sync (LAN API the Android player pulls new songs from).
    public bool MusicSyncEnabled { get; set; }

    public string MusicFolder { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

    public int MusicSyncPort { get; set; } = 47821;

    /// <summary>
    /// Legacy shared secret from before per-device pairing. Moved into the devices registry
    /// (as a hash) on start and cleared, so it no longer sits in config.json in plain text.
    /// </summary>
    public string MusicSyncToken { get; set; } = string.Empty;

    /// <summary>
    /// Where the phone app's settings/playlists backup is saved. Empty = a sibling of the music
    /// folder named "&lt;music folder&gt; Settings" (e.g. C:\Music → C:\Music Settings).
    /// </summary>
    public string MusicBackupFolder { get; set; } = string.Empty;

    public string GetMusicBackupFolder() =>
        string.IsNullOrWhiteSpace(MusicBackupFolder)
            ? MusicFolder.TrimEnd('\\', '/') + " Settings"
            : MusicBackupFolder;

    public string GetFileName()
    {
        var now = DateTime.Now;
        return FilenamePattern
            .Replace("{date}", now.ToString("yyyy-MM-dd"))
            .Replace("{time}", now.ToString("HH-mm-ss"))
            + "." + FileFormat.ToLower();
    }
}

