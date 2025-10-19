using System.IO;

namespace ScreenShotManager.Helpers;

/// <summary>
/// Helper class for file operations
/// </summary>
public static class FileHelper
{
    /// <summary>
    /// Ensures the directory for the given file path exists
    /// </summary>
    public static void EnsureDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// Checks if a file extension is an image file
    /// </summary>
    public static bool IsImageFile(string extension)
    {
        var ext = extension.ToLower();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp";
    }

    /// <summary>
    /// Attempts to delete a file, ignoring errors
    /// </summary>
    public static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Ignore deletion errors
        }
    }
}

