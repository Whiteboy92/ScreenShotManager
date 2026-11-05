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

    /// <summary>
    /// How long phones may cache the announcement. A PC that loses power can't say goodbye, so
    /// this is how long a phone can still think it's on (the mDNS default for service records is 75 min).
    /// </summary>
    internal static readonly TimeSpan PresenceTtl = TimeSpan.FromMinutes(2);

    private readonly object gate = new();
    private MulticastService? multicast;

    /// <summary>Adapters announced on (Private/Domain networks); tests allow all.</summary>
    internal Func<NetworkInterface, bool> IsHomeAdapter { get; init; } = HomeNetworks.IsHomeAdapter;

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
            // Only real LAN addresses on Private/Domain networks: by default every adapter is announced,
            // including Hyper-V/WSL ones the phone can't reach and café Wi-Fi where strangers would
            // learn the PC's name. None (e.g. only on public Wi-Fi) means no announcement at all.
            var addresses = LanAddressProvider.GetLanAddresses(IsHomeAdapter).Select(IPAddress.Parse).ToList();
            if (addresses.Count == 0)
            {
                CaptureLog.Write("[musicsync] no home network, not announcing");
                return;
            }

            profile = new ServiceProfile(
                Environment.MachineName,
                MusicSyncConstants.MdnsServiceType,
                (ushort)port,
                addresses);
            profile.AddProperty("api", MusicSyncConstants.ApiVersion.ToString());
            profile.AddProperty("fp", fingerprint);

            // The library stamps service records (PTR/SRV/TXT) with this on send; its default is 75 min.
            MulticastService.NonHostTTL = PresenceTtl;
            // Send (and answer queries) only on home adapters, not every NIC.
            multicast = new MulticastService(nics => nics.Where(IsHomeAdapter));
            discovery = new ServiceDiscovery(multicast);
            multicast.Start();
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
        try { multicast?.Dispose(); } catch { /* ignore */ }
        discovery = null;
        multicast = null;
        profile = null;
    }
}
