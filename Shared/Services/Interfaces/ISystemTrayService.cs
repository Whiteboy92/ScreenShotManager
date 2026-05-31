using System;

namespace ScreenShotManager.Shared.Services.Interfaces;

/// <summary>
/// Interface for system tray management
/// </summary>
public interface ISystemTrayService : IDisposable
{
    event EventHandler? OpenSettingsRequested;
    event EventHandler? OpenSaveFolderRequested;
    event EventHandler? ExitRequested;
    event EventHandler<bool>? PauseToggled;
    event EventHandler? TakeScreenshotRequested;
    
    void Initialize();
    void ShowNotification(string title, string message, NotificationSeverity severity = NotificationSeverity.Info);
}

