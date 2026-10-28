using System;
using System.IO;

namespace ScreenShotManager.MusicSync.Services;

/// <summary>
/// Keeps the phone inside the music folder. A junction or symbolic link placed in the folder
/// (by the user, a sync tool, an installer) could otherwise point anywhere on the PC, and its
/// files would be served as songs, lyrics or cover art. Links are therefore never followed.
/// </summary>
internal static class LibraryPaths
{
    /// <summary>
    /// True when <paramref name="path"/> is a regular file under <paramref name="root"/> with no
    /// link anywhere between them. Checked when serving, since files can change after a scan.
    /// The music folder itself may be a link; the user chose it.
    /// </summary>
    public static bool IsPlainFileInside(string root, string path)
    {
        try
        {
            var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path);
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) || !IsPlainFile(full))
            {
                return false;
            }

            for (var dir = Path.GetDirectoryName(full);
                 dir != null && dir.Length >= rootFull.Length;
                 dir = Path.GetDirectoryName(dir))
            {
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>An existing file that isn't a symbolic link (or other reparse point).</summary>
    public static bool IsPlainFile(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
