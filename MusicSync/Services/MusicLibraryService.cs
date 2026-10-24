using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ScreenShotManager.MusicSync.Models;
using ScreenShotManager.MusicSync.Services.Interfaces;
using ScreenShotManager.Shared.Helpers;

namespace ScreenShotManager.MusicSync.Services;

/// <summary>
/// Default <see cref="IMusicLibraryService"/>. Scans recursively, reuses cached hashes/tags for
/// unchanged files, and rescans after <see cref="MusicSyncConstants.RescanDebounce"/> of quiet
/// following any watcher event. Files still being copied (locked) are skipped and retried.
/// </summary>
public sealed class MusicLibraryService : IMusicLibraryService
{
    private readonly ITrackMetadataReader metadataReader;
    private readonly ITrackCache cache;
    private readonly ITrackAnalyzer? analyzer;
    private int analyzing;
    private readonly TimeSpan debounce;
    private readonly SemaphoreSlim scanGate = new(1, 1);
    private readonly Timer rescanTimer;

    private volatile LibraryManifest current = LibraryManifest.Empty;
    private volatile Dictionary<string, TrackInfo> tracksById = new();
    private volatile bool isReady;
    private string? folder;
    private FileSystemWatcher? watcher;
    private CancellationTokenSource? lifetime;

    public event EventHandler<LibraryManifest>? ManifestChanged;

    public MusicLibraryService(ITrackMetadataReader metadataReader, ITrackCache cache, ITrackAnalyzer analyzer)
        : this(metadataReader, cache, MusicSyncConstants.RescanDebounce, analyzer)
    {
    }

    internal MusicLibraryService(
        ITrackMetadataReader metadataReader,
        ITrackCache cache,
        TimeSpan debounce,
        ITrackAnalyzer? analyzer = null)
    {
        this.metadataReader = metadataReader;
        this.cache = cache;
        this.debounce = debounce;
        this.analyzer = analyzer;
        rescanTimer = new Timer(_ => _ = RescanSafeAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public LibraryManifest Current => current;

    public bool IsReady => isReady;

    public void Start(string musicFolder)
    {
        Stop();

        folder = Path.GetFullPath(musicFolder);
        lifetime = new CancellationTokenSource();

        if (Directory.Exists(folder))
        {
            watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                               | NotifyFilters.Size | NotifyFilters.LastWrite,
                // Bulk copies of an album fire many events; a bigger buffer avoids overflow.
                InternalBufferSize = 64 * 1024,
            };
            watcher.Created += OnFolderChanged;
            watcher.Changed += OnFolderChanged;
            watcher.Deleted += OnFolderChanged;
            watcher.Renamed += OnFolderChanged;
            watcher.Error += (_, e) =>
            {
                CaptureLog.Write($"[musicsync] watcher error, forcing rescan: {e.GetException().Message}");
                ScheduleRescan();
            };
            watcher.EnableRaisingEvents = true;
        }
        else
        {
            CaptureLog.Write($"[musicsync] music folder missing: {folder}");
        }

        _ = RescanSafeAsync();
    }

    public void Stop()
    {
        rescanTimer.Change(Timeout.Infinite, Timeout.Infinite);

        if (watcher != null)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            watcher = null;
        }

        lifetime?.Cancel();
        lifetime?.Dispose();
        lifetime = null;
        folder = null;
        isReady = false;
    }

    public bool TryGetTrack(string id, [NotNullWhen(true)] out TrackInfo? track) =>
        tracksById.TryGetValue(id, out track);

    public async Task RescanAsync(CancellationToken cancellationToken = default)
    {
        var root = folder;
        if (root == null)
        {
            return;
        }

        await scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (tracks, hadLockedFiles) = await Task.Run(() => Scan(root, cancellationToken), cancellationToken)
                .ConfigureAwait(false);

            // Stop() may have raced the scan; don't publish a stale folder's results.
            cancellationToken.ThrowIfCancellationRequested();

            var manifest = new LibraryManifest(
                MusicSyncConstants.ApiVersion,
                ComputeRevision(tracks),
                DateTimeOffset.UtcNow,
                tracks);

            var changed = manifest.Revision != current.Revision;
            tracksById = tracks.ToDictionary(t => t.Id);
            current = manifest;
            isReady = true;

            cache.Save();

            if (hadLockedFiles)
            {
                // Something was mid-copy; look again once it has (probably) finished.
                ScheduleRescan();
            }

            if (changed)
            {
                CaptureLog.Write($"[musicsync] library revision {manifest.Revision}: {tracks.Count} track(s)");
                ManifestChanged?.Invoke(this, manifest);
            }

            StartAnalysisIfNeeded(tracks);
        }
        finally
        {
            scanGate.Release();
        }
    }

    /// <summary>
    /// Stable, URL-safe id for a track: first 128 bits of SHA-256 over its lower-cased relative path.
    /// Path-based (not content-based) so re-tagging a song updates it instead of duplicating it.
    /// </summary>
    internal static string ComputeTrackId(string relativePath)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(relativePath.ToLowerInvariant()));
        return Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
    }

    /// <summary>Changes whenever any track is added, removed or has different content.</summary>
    internal static string ComputeRevision(IReadOnlyList<TrackInfo> tracks)
    {
        var builder = new StringBuilder();
        foreach (var track in tracks.OrderBy(t => t.Id, StringComparer.Ordinal))
        {
            // Analysis and lyrics count too, so the phone picks them up without re-downloading.
            builder.Append(track.Id).Append(':').Append(track.Sha256)
                .Append(':').Append(track.GainDb?.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(':').Append(track.LyricsVersion)
                .Append('\n');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
    }

    private (IReadOnlyList<TrackInfo> Tracks, bool HadLockedFiles) Scan(string root, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            return (Array.Empty<TrackInfo>(), false);
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };

        var tracks = new List<TrackInfo>();
        var livePaths = new List<string>();
        var hadLockedFiles = false;

        foreach (var path in Directory.EnumerateFiles(root, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!MusicSyncConstants.IsSupported(path))
            {
                continue;
            }

            try
            {
                var info = new FileInfo(path);
                var entry = GetOrBuildCacheEntry(info);
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');

                livePaths.Add(path);
                var analysis = entry.Analysis;
                tracks.Add(new TrackInfo
                {
                    Id = ComputeTrackId(relative),
                    RelativePath = relative,
                    SizeBytes = info.Length,
                    AddedUtc = new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
                    ModifiedUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                    Sha256 = entry.Sha256,
                    ContentType = MusicSyncConstants.GetContentType(path),
                    Title = entry.Metadata.Title ?? Path.GetFileNameWithoutExtension(path),
                    Artist = entry.Metadata.Artist,
                    Album = entry.Metadata.Album,
                    TrackNumber = entry.Metadata.TrackNumber,
                    DurationMs = entry.Metadata.DurationMs,
                    HasArt = entry.Metadata.HasArt,
                    GainDb = analysis?.GainDb,
                    PeakDb = analysis?.PeakDb,
                    HasWaveform = analysis != null,
                    Waveform = analysis?.Waveform,
                    LyricsVersion = LyricsVersionFor(path, entry),
                    FullPath = path,
                });
            }
            catch (IOException)
            {
                // Locked by a copy in progress (or vanished mid-scan). Picked up on the retry.
                hadLockedFiles = true;
            }
            catch (UnauthorizedAccessException)
            {
                // Not readable by this user; leave it out.
            }
        }

        cache.Retain(livePaths);
        return (WithAlbumGain(tracks), hadLockedFiles);
    }

    /// <summary>A sibling .lrc wins over tag lyrics; its size+time make the version change on edits.</summary>
    private static string? LyricsVersionFor(string path, CachedTrack entry)
    {
        var lrc = new FileInfo(TrackMetadataReader.LrcPathFor(path));
        if (lrc.Exists)
        {
            return $"lrc-{lrc.Length}-{lrc.LastWriteTimeUtc.Ticks}";
        }

        return entry.Metadata.HasEmbeddedLyrics ? $"tag-{entry.Sha256[..12]}" : null;
    }

    private static IReadOnlyList<TrackInfo> WithAlbumGain(List<TrackInfo> tracks)
    {
        var albumGains = tracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Album) && t.GainDb != null && t.DurationMs != null)
            .GroupBy(t => t.Album!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => TrackAnalysis.AlbumGainDb(g.Select(t => (TrackAnalysis.ReferenceLufs - t.GainDb!.Value, t.DurationMs!.Value))),
                StringComparer.OrdinalIgnoreCase);

        return tracks
            .Select(t => t.Album != null && albumGains.TryGetValue(t.Album, out var gain) ? t with { AlbumGainDb = gain } : t)
            .ToList();
    }

    /// <summary>
    /// Analyses songs that don't have loudness/waveform yet, two at a time at low priority, and
    /// republishes the manifest every 25 songs so the phone gets results as they come in.
    /// </summary>
    private void StartAnalysisIfNeeded(IReadOnlyList<TrackInfo> tracks)
    {
        if (analyzer is not { IsAvailable: true } || tracks.All(t => t.HasWaveform))
        {
            return;
        }

        if (Interlocked.CompareExchange(ref analyzing, 1, 0) != 0)
        {
            return;
        }

        var token = lifetime?.Token ?? CancellationToken.None;
        var todo = tracks.Where(t => !t.HasWaveform).Select(t => t.FullPath).ToList();
        _ = Task.Run(async () =>
        {
            var done = 0;
            try
            {
                await Parallel.ForEachAsync(
                    todo,
                    new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = token },
                    async (path, ct) =>
                    {
                        var info = new FileInfo(path);
                        if (!info.Exists || !cache.TryGet(path, info.Length, info.LastWriteTimeUtc, out var entry))
                        {
                            return;
                        }

                        var analysis = await analyzer.AnalyzeAsync(path, ct).ConfigureAwait(false)
                                       // Undecodable/silent: store an empty result so we don't retry forever.
                                       ?? new TrackAnalysis(TrackAnalysis.ReferenceLufs, 0, new byte[MusicSyncConstants.WaveformPoints]);
                        cache.Set(path, entry with { Analysis = analysis });

                        if (Interlocked.Increment(ref done) % 25 == 0)
                        {
                            cache.Save();
                            ScheduleRescan();
                        }
                    }).ConfigureAwait(false);
                CaptureLog.Write($"[musicsync] analysed {done} song(s)");
            }
            catch (OperationCanceledException)
            {
                // Stopped.
            }
            catch (Exception ex)
            {
                CaptureLog.Write($"[musicsync] analysis failed: {ex.Message}");
            }
            finally
            {
                cache.Save();
                Interlocked.Exchange(ref analyzing, 0);
                if (!token.IsCancellationRequested)
                {
                    ScheduleRescan();
                }
            }
        }, token);
    }

    private CachedTrack GetOrBuildCacheEntry(FileInfo info)
    {
        if (cache.TryGet(info.FullName, info.Length, info.LastWriteTimeUtc, out var cached))
        {
            if (cached.MetadataVersion >= MusicSyncConstants.MetadataVersion)
            {
                return cached;
            }

            // Tag reader learned something new (e.g. lyrics): re-read tags, keep hash and analysis.
            var upgraded = cached with
            {
                Metadata = metadataReader.Read(info.FullName),
                MetadataVersion = MusicSyncConstants.MetadataVersion,
            };
            cache.Set(info.FullName, upgraded);
            return upgraded;
        }

        string sha;
        // FileShare.Read fails while a writer still holds the file, which is exactly the signal we want.
        using (var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
                   bufferSize: 1024 * 1024, FileOptions.SequentialScan))
        {
            sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        var entry = new CachedTrack(info.Length, info.LastWriteTimeUtc.Ticks, sha, metadataReader.Read(info.FullName))
        {
            MetadataVersion = MusicSyncConstants.MetadataVersion,
        };
        cache.Set(info.FullName, entry);
        return entry;
    }

    private void OnFolderChanged(object sender, FileSystemEventArgs e) => ScheduleRescan();

    private void ScheduleRescan() => rescanTimer.Change(debounce, Timeout.InfiniteTimeSpan);

    private async Task RescanSafeAsync()
    {
        var token = lifetime?.Token ?? CancellationToken.None;
        try
        {
            await RescanAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopped mid-scan.
        }
        catch (Exception ex)
        {
            CaptureLog.Write($"[musicsync] rescan failed: {ex}");
        }
    }

    public void Dispose()
    {
        Stop();
        rescanTimer.Dispose();
        scanGate.Dispose();
    }
}
