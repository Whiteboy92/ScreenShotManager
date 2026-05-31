using System;
using System.IO;

namespace ScreenShotManager.Shared.Helpers;

/// <summary>
/// Lightweight append-only diagnostic log for the screen-capture pipeline.
/// Writes to %AppData%\ScreenShotManager\hdr-capture.log. Best-effort; never throws.
/// </summary>
public static class CaptureLog
{
    private static readonly object Sync = new();

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ScreenShotManager",
        "hdr-capture.log");

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // diagnostics must never break capture
        }
    }

    public static void Session(string header)
    {
        Write("");
        Write("===== " + header + " =====");
    }
}
