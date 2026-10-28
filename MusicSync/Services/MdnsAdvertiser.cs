using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using Makaretu.Dns;
using Microsoft.Win32;
using ScreenShotManager.MusicSync.Models;
using ScreenShotManager.MusicSync.Services.Interfaces;
using ScreenShotManager.Shared.Helpers;

namespace ScreenShotManager.MusicSync.Services;

/// <summary>
/// DNS-SD advertiser for <c>_musicsync._tcp</c>. TXT records carry the API version and certificate
/// fingerprint (never the token), so the phone can tell its paired PC apart from others.
/// The announcement is the phone's "PC is on" signal: it only syncs automatically after hearing it,
/// so it is re-sent whenever the PC wakes up or its network changes, and a goodbye goes out on stop.
/// </summary>
public sealed class MdnsAdvertiser : IMdnsAdvertiser
{
    /// <summary>Network changes come in bursts (DHCP, adapters coming up); announce once they settle.</summary>
    private static readonly TimeSpan ReannounceDelay = TimeSpan.FromSeconds(5);

    private readonly object gate = new();
    private ServiceDiscovery? discovery;
    private ServiceProfile? profile;
    private (int Port, string Fingerprint)? current;
    private Timer? reannounceTimer;
    private bool subscribed;

    public void Start(int port, string certificateFingerprint)
    {
        lock (gate)
        {
            current = (port, certificateFingerprint);
            AdvertiseLocked();
            if (!subscribed)
            {
                NetworkChange.NetworkAddressChanged += OnNetworkChanged;
                SystemEvents.PowerModeChanged += OnPowerModeChanged;
                subscribed = true;
            }
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            current = null;
            if (subscribed)
            {
                NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                subscribed = false;
            }

            reannounceTimer?.Dispose();
            reannounceTimer = null;
            UnadvertiseLocked();
        }
    }

    public void Dispose() => Stop();

    private void OnNetworkChanged(object? sender, EventArgs e) => ScheduleReannounce();

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            ScheduleReannounce();
        }
    }

    private void ScheduleReannounce()
    {
        lock (gate)
        {
            if (current == null)
            {
                return;
            }

            reannounceTimer?.Dispose();
            reannounceTimer = new Timer(_ =>
            {
                lock (gate)
                {
                    if (current != null)
                    {
                        // Rebuilt from scratch: the LAN address list may have changed too.
                        AdvertiseLocked();
                    }
                }
            }, null, ReannounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void AdvertiseLocked()
    {
        UnadvertiseLocked();
        if (current is not var (port, fingerprint))
        {
            return;
        }

        try
        {
            // Only real LAN addresses: by default every adapter is announced, including Hyper-V/WSL
            // ones (e.g. 172.20.x.x) that the phone can't reach.
            var addresses = LanAddressProvider.GetLanAddresses().Select(IPAddress.Parse).ToList();
            profile = new ServiceProfile(
                Environment.MachineName,
                MusicSyncConstants.MdnsServiceType,
                (ushort)port,
                addresses.Count > 0 ? addresses : null);
            profile.AddProperty("api", MusicSyncConstants.ApiVersion.ToString());
            profile.AddProperty("fp", fingerprint);

            discovery = new ServiceDiscovery();
            discovery.Advertise(profile);
            discovery.Announce(profile);
        }
        catch (Exception ex)
        {
            // UDP 5353 can be unavailable; pairing via QR still works without discovery.
            CaptureLog.Write($"[musicsync] mDNS advertise failed: {ex.Message}");
            UnadvertiseLocked();
        }
    }

    private void UnadvertiseLocked()
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
}
