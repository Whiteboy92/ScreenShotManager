using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ScreenShotManager.MusicSync.Services;

/// <summary>Lists this PC's private IPv4 addresses on active Ethernet/Wi-Fi adapters of Private/Domain networks, for the pairing QR code and mDNS.</summary>
public static class LanAddressProvider
{
    public static IReadOnlyList<string> GetLanAddresses(Func<NetworkInterface, bool>? isHomeAdapter = null)
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                              && nic.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                                  or NetworkInterfaceType.Wireless80211
                                  or NetworkInterfaceType.GigabitEthernet)
                // Hyper-V / WSL / VPN adapters report as Ethernet; the phone can't reach those.
                .Where(nic => !IsVirtual(nic))
                // Café/hotel Wi-Fi (a Public network) is never offered or announced.
                .Where(isHomeAdapter ?? HomeNetworks.IsHomeAdapter)
                // Adapters with a default gateway first: that's the one the phone's Wi-Fi shares.
                .OrderByDescending(nic => nic.GetIPProperties().GatewayAddresses.Count > 0)
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && IsPrivate(a.GetAddressBytes()))
                .Select(a => a.ToString())
                .Distinct()
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    internal static bool IsPrivate(byte[] ipv4) => ipv4 switch
    {
        [10, ..] => true,
        [172, >= 16 and <= 31, ..] => true,
        [192, 168, ..] => true,
        _ => false,
    };

    private static bool IsVirtual(NetworkInterface nic)
    {
        var description = nic.Description + " " + nic.Name;
        return new[] { "Hyper-V", "vEthernet", "VirtualBox", "VMware", "WSL", "TAP-", "WireGuard", "Tailscale" }
            .Any(marker => description.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
