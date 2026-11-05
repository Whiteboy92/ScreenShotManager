using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ScreenShotManager.MusicSync.Models;

namespace ScreenShotManager.MusicSync.Services;

/// <summary>
/// One settings backup per paired phone (<c>musicwave-backup.&lt;device id&gt;.json</c>), so a phone
/// only ever reads and overwrites its own. Moving a backup to another phone (e.g. after a reinstall,
/// which pairs as a new device) is the PC owner's call, made in the Pair window.
/// </summary>
internal static class BackupFiles
{
    private const string Prefix = "musicwave-backup.";
    private const string PreviousSuffix = ".previous.json";

    /// <summary>A backup the owner can hand to a phone: another device's, or the pre-per-device shared one.</summary>
    public sealed record Source(string Path, string? DeviceId, DateTime SavedUtc);

    public static string PathFor(string folder, string deviceId) =>
        Path.Combine(folder, $"{Prefix}{CheckId(deviceId)}.json");

    public static string PreviousPathFor(string folder, string deviceId) =>
        Path.Combine(folder, $"{Prefix}{CheckId(deviceId)}{PreviousSuffix}");

    /// <summary>The single file every phone shared before backups were per device.</summary>
    public static string LegacyPath(string folder) => Path.Combine(folder, MusicSyncConstants.BackupFileName);

    /// <summary>Atomically writes <paramref name="deviceId"/>'s backup, keeping its previous one alongside.</summary>
    public static void Write(string folder, string deviceId, byte[] json)
    {
        Directory.CreateDirectory(folder);
        var target = PathFor(folder, deviceId);
        var temp = target + ".tmp";
        File.WriteAllBytes(temp, json);
        if (LibraryPaths.IsPlainFile(target))
        {
            File.Copy(target, PreviousPathFor(folder, deviceId), overwrite: true);
        }

        File.Move(temp, target, overwrite: true);
    }

    /// <summary>Every stored backup except <paramref name="exceptDeviceId"/>'s own, newest first.</summary>
    public static IReadOnlyList<Source> Sources(string? folder, string exceptDeviceId)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return Array.Empty<Source>();
        }

        var sources = new List<Source>();
        foreach (var path in Directory.EnumerateFiles(folder, Prefix + "*"))
        {
            var name = Path.GetFileName(path);
            if (!name.EndsWith(".json", StringComparison.Ordinal)
                || name.EndsWith(PreviousSuffix, StringComparison.OrdinalIgnoreCase)
                || !LibraryPaths.IsPlainFile(path))
            {
                continue;
            }

            if (string.Equals(name, MusicSyncConstants.BackupFileName, StringComparison.OrdinalIgnoreCase))
            {
                sources.Add(new Source(path, null, File.GetLastWriteTimeUtc(path)));
                continue;
            }

            var id = name[Prefix.Length..^".json".Length];
            if (IsValidId(id) && id != exceptDeviceId)
            {
                sources.Add(new Source(path, id, File.GetLastWriteTimeUtc(path)));
            }
        }

        return sources.OrderByDescending(s => s.SavedUtc).ToList();
    }

    /// <summary>Makes <paramref name="source"/> the backup of <paramref name="deviceId"/> (its current one becomes the previous copy).</summary>
    public static void CopyTo(string folder, Source source, string deviceId)
    {
        if (!LibraryPaths.IsPlainFile(source.Path))
        {
            throw new FileNotFoundException("Backup is gone", source.Path);
        }

        Write(folder, deviceId, File.ReadAllBytes(source.Path));
    }

    /// <summary>Device ids are 12 lower-case hex chars (<see cref="DeviceRegistry"/>); anything else never becomes a file name.</summary>
    internal static bool IsValidId(string id) =>
        id.Length is > 0 and <= 32 && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string CheckId(string deviceId) =>
        IsValidId(deviceId) ? deviceId : throw new ArgumentException("Bad device id", nameof(deviceId));
}
