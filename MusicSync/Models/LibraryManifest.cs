using System;
using System.Collections.Generic;

namespace ScreenShotManager.MusicSync.Models;

/// <summary>
/// Snapshot of the music folder served at <c>GET /api/manifest</c>. <see cref="Revision"/> is the
/// ETag: the phone sends it back as If-None-Match and gets 304 when nothing changed.
/// </summary>
public sealed record LibraryManifest(
    int ApiVersion,
    string Revision,
    DateTimeOffset GeneratedUtc,
    IReadOnlyList<TrackInfo> Tracks)
{
    public static LibraryManifest Empty { get; } =
        new(MusicSyncConstants.ApiVersion, "empty", DateTimeOffset.MinValue, Array.Empty<TrackInfo>());
}
