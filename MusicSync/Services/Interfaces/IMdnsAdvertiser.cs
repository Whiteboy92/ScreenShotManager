using System;

namespace ScreenShotManager.MusicSync.Services.Interfaces;

/// <summary>
/// Announces the API on the LAN via DNS-SD (<c>_musicsync._tcp</c>). The phone treats the
/// announcement as "the PC is on": automatic syncs only start after hearing it, and it also finds
/// the PC after its IP changes. Routers with AP isolation block it; the phone's manual sync button
/// still connects to the hosts from the pairing QR code then.
/// </summary>
public interface IMdnsAdvertiser : IDisposable
{
    void Start(int port, string certificateFingerprint);

    void Stop();
}
