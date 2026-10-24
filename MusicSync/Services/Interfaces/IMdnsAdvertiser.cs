using System;

namespace ScreenShotManager.MusicSync.Services.Interfaces;

/// <summary>
/// Announces the API on the LAN via DNS-SD (<c>_musicsync._tcp</c>) so the phone can find a
/// paired PC after its IP changes. Best-effort: routers with AP isolation block it, and the phone
/// then falls back to the hosts from the pairing QR code.
/// </summary>
public interface IMdnsAdvertiser : IDisposable
{
    void Start(int port, string certificateFingerprint);

    void Stop();
}
