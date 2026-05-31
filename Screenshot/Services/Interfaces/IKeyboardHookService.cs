using System;

namespace ScreenShotManager.Screenshot.Services.Interfaces;

/// <summary>
/// Interface for global keyboard hook service
/// </summary>
public interface IKeyboardHookService : IDisposable
{
    event EventHandler? PrintScreenPressed;
    bool IsPaused { get; set; }
    void Start();
}

