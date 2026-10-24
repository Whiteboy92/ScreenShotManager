using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using ScreenShotManager.MusicSync.Services.Interfaces;
using ScreenShotManager.Shared.Helpers;

namespace ScreenShotManager.MusicSync.Services;

/// <summary>
/// JSON-backed <see cref="ITrackCache"/> at %AppData%\ScreenShotManager\musicsync-cache.json.
/// A missing or corrupt file just means a full rescan; it never blocks startup.
/// </summary>
public sealed class TrackCache : ITrackCache
{
    private static readonly string DefaultCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ScreenShotManager",
        "musicsync-cache.json");

    private readonly object sync = new();
    private readonly string cachePath;
    private Dictionary<string, CachedTrack>? entries;
    private bool dirty;

    public TrackCache()
        : this(DefaultCachePath)
    {
    }

    internal TrackCache(string cachePath)
    {
        this.cachePath = cachePath;
    }

    public bool TryGet(string fullPath, long sizeBytes, DateTime modifiedUtc, [NotNullWhen(true)] out CachedTrack? entry)
    {
        lock (sync)
        {
            if (Entries.TryGetValue(fullPath, out var cached)
                && cached.SizeBytes == sizeBytes
                && cached.ModifiedUtcTicks == modifiedUtc.Ticks)
            {
                entry = cached;
                return true;
            }
        }

        entry = null;
        return false;
    }

    public void Set(string fullPath, CachedTrack entry)
    {
        lock (sync)
        {
            Entries[fullPath] = entry;
            dirty = true;
        }
    }

    public void Retain(IReadOnlyCollection<string> livePaths)
    {
        lock (sync)
        {
            var live = new HashSet<string>(livePaths, StringComparer.OrdinalIgnoreCase);
            foreach (var stale in Entries.Keys.Where(k => !live.Contains(k)).ToList())
            {
                Entries.Remove(stale);
                dirty = true;
            }
        }
    }

    public void Save()
    {
        string json;
        lock (sync)
        {
            if (!dirty || entries == null)
            {
                return;
            }

            json = JsonSerializer.Serialize(entries);
            dirty = false;
        }

        try
        {
            FileHelper.EnsureDirectoryExists(cachePath);
            var temp = cachePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, cachePath, overwrite: true);
        }
        catch (Exception ex)
        {
            CaptureLog.Write($"[musicsync] cache save failed: {ex.Message}");
        }
    }

    /// <summary>Lazily loaded so construction stays cheap. Callers hold <see cref="sync"/>.</summary>
    private Dictionary<string, CachedTrack> Entries => entries ??= Load();

    private Dictionary<string, CachedTrack> Load()
    {
        try
        {
            if (File.Exists(cachePath))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, CachedTrack>>(File.ReadAllText(cachePath));
                if (loaded != null)
                {
                    return new Dictionary<string, CachedTrack>(loaded, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch (Exception ex)
        {
            CaptureLog.Write($"[musicsync] cache load failed, rescanning from scratch: {ex.Message}");
        }

        return new Dictionary<string, CachedTrack>(StringComparer.OrdinalIgnoreCase);
    }
}
