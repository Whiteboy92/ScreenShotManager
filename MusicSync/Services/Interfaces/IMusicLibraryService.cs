using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using ScreenShotManager.MusicSync.Models;

namespace ScreenShotManager.MusicSync.Services.Interfaces;

/// <summary>
/// Scans the music folder into a <see cref="LibraryManifest"/>, watches it for changes and rescans
/// (debounced) so newly copied songs show up without a restart.
/// </summary>
public interface IMusicLibraryService : IDisposable
{
    /// <summary>Raised on a worker thread after a rescan produced a different manifest.</summary>
    event EventHandler<LibraryManifest>? ManifestChanged;

    /// <summary>Latest completed scan; <see cref="LibraryManifest.Empty"/> until the first one finishes.</summary>
    LibraryManifest Current { get; }

    /// <summary>False until the first scan after <see cref="Start"/> has completed.</summary>
    bool IsReady { get; }

    /// <summary>Starts watching <paramref name="folder"/> and kicks off the initial scan in the background.</summary>
    void Start(string folder);

    void Stop();

    /// <summary>Rescans now. Concurrent calls are serialized.</summary>
    Task RescanAsync(CancellationToken cancellationToken = default);

    bool TryGetTrack(string id, [NotNullWhen(true)] out TrackInfo? track);
}
