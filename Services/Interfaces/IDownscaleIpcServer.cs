using System;

namespace ScreenShotManager.Services.Interfaces;

/// <summary>
/// Named-pipe server hosted by the primary instance. Receives file paths forwarded by
/// short-lived <c>--downscale</c> launches and surfaces them to the app.
/// </summary>
public interface IDownscaleIpcServer : IDisposable
{
    /// <summary>Raised (on a background thread) for each file path received from a client.</summary>
    event EventHandler<string>? PathReceived;

    /// <summary>Begins accepting pipe connections.</summary>
    void Start();
}
