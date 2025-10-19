using ScreenShotManager.Models;

namespace ScreenShotManager.Services.Interfaces;

/// <summary>
/// Interface for settings management
/// </summary>
public interface ISettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
    void SetStartupRegistry(bool enable);
    void CleanupOldFiles(AppSettings settings);
}

