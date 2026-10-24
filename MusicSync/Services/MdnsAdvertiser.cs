using System;
using Makaretu.Dns;
using ScreenShotManager.MusicSync.Models;
using ScreenShotManager.MusicSync.Services.Interfaces;
using ScreenShotManager.Shared.Helpers;

namespace ScreenShotManager.MusicSync.Services;

/// <summary>
/// DNS-SD advertiser for <c>_musicsync._tcp</c>. TXT records carry the API version and certificate
/// fingerprint (never the token), so the phone can tell its paired PC apart from others.
/// </summary>
public sealed class MdnsAdvertiser : IMdnsAdvertiser
{
    private ServiceDiscovery? discovery;
    private ServiceProfile? profile;

    public void Start(int port, string certificateFingerprint)
    {
        Stop();

        try
        {
            profile = new ServiceProfile(Environment.MachineName, MusicSyncConstants.MdnsServiceType, (ushort)port);
            profile.AddProperty("api", MusicSyncConstants.ApiVersion.ToString());
            profile.AddProperty("fp", certificateFingerprint);

            discovery = new ServiceDiscovery();
            discovery.Advertise(profile);
            discovery.Announce(profile);
        }
        catch (Exception ex)
        {
            // UDP 5353 can be unavailable; pairing via QR still works without discovery.
            CaptureLog.Write($"[musicsync] mDNS advertise failed: {ex.Message}");
            Stop();
        }
    }

    public void Stop()
    {
        try
        {
            if (discovery != null && profile != null)
            {
                discovery.Unadvertise(profile);
            }
        }
        catch
        {
            // Best-effort goodbye packet.
        }

        try { discovery?.Dispose(); } catch { /* ignore */ }
        discovery = null;
        profile = null;
    }

    public void Dispose() => Stop();
}
