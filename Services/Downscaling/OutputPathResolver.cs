using System.Globalization;
using System.IO;

namespace ScreenShotManager.Services.Downscaling;

/// <summary>
/// Builds the output path next to the source: <c>movie.downscaled.mp4</c>, falling back to
/// <c>movie.downscaled (1).mp4</c>, <c>(2)</c>… when the target already exists.
/// </summary>
internal static class OutputPathResolver
{
    public static string Resolve(string sourcePath)
    {
        var dir = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var ext = Path.GetExtension(sourcePath);

        var candidate = Path.Combine(dir, $"{name}.downscaled{ext}");
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        for (var i = 1; ; i++)
        {
            var suffix = i.ToString(CultureInfo.InvariantCulture);
            candidate = Path.Combine(dir, $"{name}.downscaled ({suffix}){ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }
}
