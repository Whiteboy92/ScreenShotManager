using System;
using System.Collections.Generic;

namespace ScreenShotManager.Models.Downscaling;

/// <summary>
/// Shared constants for the "Downscale Video" feature: CLI verb, IPC names and supported formats.
/// </summary>
public static class DownscaleConstants
{
    /// <summary>Command-line switch Explorer passes: <c>MyApp.exe --downscale "%1"</c>.</summary>
    public const string CliArgument = "--downscale";

    /// <summary>Named pipe used to forward file paths to the already-running primary instance.</summary>
    public const string PipeName = "ScreenShotManager.Downscale.Pipe.v1";

    /// <summary>Per-user mutex name used for single-instance detection.</summary>
    public const string SingleInstanceMutexName = "Local\\ScreenShotManager.SingleInstance.v1";

    /// <summary>Registry verb id created under each extension's SystemFileAssociations key.</summary>
    public const string ContextMenuVerb = "ScreenShotManager.DownscaleVideo";

    /// <summary>Label shown in the Explorer right-click menu.</summary>
    public const string ContextMenuLabel = "Downscale Video";

    /// <summary>Video extensions (lower-case, leading dot) the feature accepts.</summary>
    public static readonly HashSet<string> SupportedExtensions = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".mov", ".webm", ".avi", ".m4v", ".ts",
    };

    /// <summary>True when <paramref name="path"/> has a supported video extension.</summary>
    public static bool IsSupported(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext);
    }
}
