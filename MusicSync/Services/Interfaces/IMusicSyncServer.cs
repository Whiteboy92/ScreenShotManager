using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenShotManager.MusicSync.Services.Interfaces;

/// <summary>
/// HTTPS LAN API (Kestrel, in-process) the Android player syncs from. Every route except
/// <c>/api/ping</c> requires <c>Authorization: Bearer &lt;token&gt;</c>.
/// </summary>
public interface IMusicSyncServer : IAsyncDisposable
{
    bool IsRunning { get; }

    /// <summary>Actual listening port (differs from the requested one only when 0 was requested).</summary>
    int BoundPort { get; }

    /// <summary>Binds all interfaces on <paramref name="port"/>. Throws if the port is taken.</summary>
    Task StartAsync(int port, string token, X509Certificate2 certificate, CancellationToken cancellationToken = default);

    Task StopAsync();

    /// <summary>
    /// Folder where <c>PUT /api/backup</c> stores the phone's settings backup. Null disables backups (503).
    /// </summary>
    string? BackupFolder { get; set; }

    /// <summary>Swaps the accepted token (re-pairing); phones holding the old one get 401.</summary>
    void UpdateToken(string token);
}
