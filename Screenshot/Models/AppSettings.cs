using System;
using System.IO;

namespace ScreenShotManager.Screenshot.Models;

/// <summary>
/// Application settings model
/// </summary>
public class AppSettings
{
    public string SaveFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "Screenshots"
    );

    public string FileFormat { get; set; } = "PNG";
    public string FilenamePattern { get; set; } = "Screenshot_{date}_{time}";
    public bool DisableDimming { get; set; }
    public bool StartOnLogin { get; set; }
    public int MaxAutoSaveHistory { get; set; } = 100;

    public string GetFileName()
    {
        var now = DateTime.Now;
        return FilenamePattern
            .Replace("{date}", now.ToString("yyyy-MM-dd"))
            .Replace("{time}", now.ToString("HH-mm-ss"))
            + "." + FileFormat.ToLower();
    }
}

