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
    event EventHandler? PairPhoneRequested;
    
    void Initialize();
    void ShowNotification(string title, string message, NotificationSeverity severity = NotificationSeverity.Info);

    /// <summary>
    /// Sets the tray icon hover tooltip. Pass null/empty to restore the default text.
    /// Used to surface live downscale progress. Must be called on the UI thread.
    /// </summary>
    void SetStatus(string? status);
}

