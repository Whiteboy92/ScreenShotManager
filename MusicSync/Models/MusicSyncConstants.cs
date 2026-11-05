using System;
using System.Collections.Generic;

namespace ScreenShotManager.MusicSync.Models;

/// <summary>
/// Shared constants for the "Music Sync" feature: LAN API version, discovery names and audio formats.
/// </summary>
public static class MusicSyncConstants
{
    /// <summary>Bumped on breaking API changes so the phone can refuse an incompatible PC.</summary>
    public const int ApiVersion = 1;

    /// <summary>
    /// Version of the pairing QR/link format. 2: carries a one-time <c>code</c> the phone trades
    /// for its own token via <c>POST /api/pair</c> (1 carried the shared token itself).
    /// </summary>
    public const int PairingVersion = 2;

    /// <summary>How long a pairing code shown in the QR stays valid.</summary>
    public static readonly TimeSpan PairingCodeLifetime = TimeSpan.FromMinutes(5);

    /// <summary>DNS-SD service type the phone browses for (Android <c>NsdManager</c>).</summary>
    public const string MdnsServiceType = "_musicsync._tcp";

    /// <summary>URI scheme encoded in the pairing QR code.</summary>
    public const string PairingScheme = "musicsync";

    /// <summary>Bytes of randomness in a generated pairing token (hex-encoded, so 64 chars).</summary>
    public const int TokenBytes = 32;

    /// <summary>
    /// The settings backup every phone shared before backups were per device. Now each phone has
    /// <c>musicwave-backup.&lt;device id&gt;.json</c> (see BackupFiles); this one is only handed out.
    /// </summary>
    public const string BackupFileName = "musicwave-backup.json";

    /// <summary>Largest accepted backup upload (playlists for tens of thousands of songs fit easily).</summary>
    public const int MaxBackupBytes = 2 * 1024 * 1024;

    /// <summary>Peaks per song in the waveform the phone draws as its seek bar.</summary>
    public const int WaveformPoints = 300;

    /// <summary>Bump to make every cached song's tags be re-read (e.g. a new tag field), without re-hashing.</summary>
    public const int MetadataVersion = 3;

    /// <summary>Simultaneous connections Kestrel accepts (the phone uses a handful; Android Auto adds art requests).</summary>
    public const int MaxConnections = 32;

    /// <summary>Connections one address may hold at once, so a single LAN peer can't take every slot.</summary>
    public const int MaxConnectionsPerAddress = 8;

    /// <summary>Idle time before Kestrel closes a kept-alive connection (the phone reconnects cheaply).</summary>
    public static readonly TimeSpan KeepAliveTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Time a client gets to send its request headers.</summary>
    public static readonly TimeSpan RequestHeadersTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Wrong tokens from one address within <see cref="AuthFailureWindow"/> before it is locked out.</summary>
    public const int MaxAuthFailures = 5;

    public static readonly TimeSpan AuthFailureWindow = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan AuthLockout = TimeSpan.FromMinutes(5);

    /// <summary>Quiet period after the last file-system change before the library is rescanned.</summary>
    public static readonly TimeSpan RescanDebounce = TimeSpan.FromSeconds(3);

    /// <summary>Audio extensions (lower-case, leading dot) the library exposes.</summary>
    public static readonly HashSet<string> SupportedExtensions = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wav",
    };

    /// <summary>MIME types served for each supported extension.</summary>
    public static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg",
        [".flac"] = "audio/flac",
        [".m4a"] = "audio/mp4",
        [".aac"] = "audio/aac",
        [".ogg"] = "audio/ogg",
        [".opus"] = "audio/opus",
        [".wav"] = "audio/wav",
    };

    /// <summary>True when <paramref name="path"/> has a supported audio extension.</summary>
    public static bool IsSupported(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext);
    }

    public static string GetContentType(string path) =>
        ContentTypes.TryGetValue(System.IO.Path.GetExtension(path), out var type)
            ? type
            : "application/octet-stream";
}
