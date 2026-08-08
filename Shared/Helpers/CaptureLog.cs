using System;
using System.IO;
using System.Text;

namespace ScreenShotManager.Shared.Helpers;

/// <summary>
/// Lightweight append-only diagnostic log for the screen-capture pipeline.
/// Writes to %AppData%\ScreenShotManager\hdr-capture.log. Best-effort; never throws.
///
/// Supports batching: <see cref="BeginBatch"/> accumulates lines in memory so the hot
/// capture path does no file I/O, then <see cref="Flush"/> writes them in a single append.
/// </summary>
public static class CaptureLog
{
    private static readonly object Sync = new();

    private static StringBuilder? batch;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ScreenShotManager",
        "hdr-capture.log");

    /// <summary>Starts buffering log lines in memory instead of writing each to disk.</summary>
    public static void BeginBatch()
    {
        lock (Sync)
        {
            batch = new StringBuilder();
        }
    }

    /// <summary>Writes any buffered lines to disk in one append and stops buffering.</summary>
    public static void Flush()
    {
        string? payload;
        lock (Sync)
        {
            if (batch == null)
            {
                return;
            }

            payload = batch.Length > 0 ? batch.ToString() : null;
            batch = null;
        }

        if (payload != null)
        {
            AppendToFile(payload);
        }
    }

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}";

        lock (Sync)
        {
            if (batch != null)
            {
                batch.Append(line);
                return;
            }
        }

        AppendToFile(line);
    }

    public static void Session(string header)
    {
        Write("");
        Write("===== " + header + " =====");
    }

    private static void AppendToFile(string text)
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

                File.AppendAllText(LogPath, text);
            }
        }
        catch
        {
            // diagnostics must never break capture
        }
    }
}
