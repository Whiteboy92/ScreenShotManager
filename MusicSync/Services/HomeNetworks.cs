using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using ScreenShotManager.Shared.Helpers;

namespace ScreenShotManager.MusicSync.Services;

/// <summary>
/// Which network adapters are on a network Windows classes as Private or Domain (the "home or work"
/// choice from the first-connect prompt). Café and hotel Wi-Fi use private address ranges too, so
/// the address alone can't tell them apart; the network category can. Read from the Network List
/// Manager, cached briefly and dropped whenever addresses change. Unknown adapters count as Public.
/// </summary>
internal static class HomeNetworks
{
    /// <summary>NLM_NETWORK_CATEGORY_PUBLIC; Private is 1, DomainAuthenticated 2.</summary>
    private const int PublicCategory = 0;

    private static readonly Guid NetworkListManagerClsid = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);
    private static readonly object Gate = new();
    private static HashSet<string>? homeAdapterIds;
    private static DateTime readUtc;
    private static bool subscribed;

    /// <summary>True for loopback, or an address assigned to an adapter on a Private/Domain network.</summary>
    public static bool IsHomeAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var homeIds = HomeAdapterIds();
        return NetworkInterface.GetAllNetworkInterfaces().Any(nic =>
            homeIds.Contains(nic.Id) && nic.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(address)));
    }

    /// <summary>True when <paramref name="nic"/> is on a Private/Domain network.</summary>
    public static bool IsHomeAdapter(NetworkInterface nic) => HomeAdapterIds().Contains(nic.Id);

    private static HashSet<string> HomeAdapterIds()
    {
        lock (Gate)
        {
            if (!subscribed)
            {
                NetworkChange.NetworkAddressChanged += (_, _) => { lock (Gate) { homeAdapterIds = null; } };
                subscribed = true;
            }

            if (homeAdapterIds == null || DateTime.UtcNow - readUtc > CacheLifetime)
            {
                homeAdapterIds = ReadHomeAdapterIds();
                readUtc = DateTime.UtcNow;
            }

            return homeAdapterIds;
        }
    }

    /// <summary>NetworkInterface.Id is the adapter GUID in "{...}" form, as NLM's GetAdapterId returns it.</summary>
    private static HashSet<string> ReadHomeAdapterIds()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // The NLM interfaces are dual (IDispatch), so late binding avoids hand-written COM vtables.
            dynamic manager = Activator.CreateInstance(Type.GetTypeFromCLSID(NetworkListManagerClsid, throwOnError: true)!)!;
            foreach (dynamic connection in manager.GetNetworkConnections())
            {
                if ((int)connection.GetNetwork().GetCategory() != PublicCategory)
                {
                    ids.Add(((Guid)connection.GetAdapterId()).ToString("B"));
                }
            }
        }
        catch (Exception ex)
        {
            // Fail closed: without the category, only loopback is served.
            CaptureLog.Write($"[musicsync] could not read network categories, LAN serving off: {ex.Message}");
        }

        return ids;
    }
}
