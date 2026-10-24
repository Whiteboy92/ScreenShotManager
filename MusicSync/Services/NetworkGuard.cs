using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using ScreenShotManager.MusicSync.Models;

namespace ScreenShotManager.MusicSync.Services;

/// <summary>
/// Who may talk to the music API at all: only devices on the home network. The server still binds
/// every interface (so a new DHCP address or a reconnecting Wi-Fi adapter keeps working without a
/// restart), but connections whose either end isn't loopback/private are dropped before TLS.
/// </summary>
public static class NetworkGuard
{
    /// <summary>Loopback, RFC 1918 IPv4, IPv6 link-local (fe80::/10) and unique-local (fc00::/7).</summary>
    public static bool IsLocalNetwork(IPAddress? address)
    {
        if (address == null)
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return LanAddressProvider.IsPrivate(address.GetAddressBytes());
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return address.IsIPv6LinkLocal || (bytes[0] & 0xFE) == 0xFC;
        }

        return false;
    }
}

/// <summary>
/// Locks out a remote address after <see cref="MusicSyncConstants.MaxAuthFailures"/> wrong tokens
/// within <see cref="MusicSyncConstants.AuthFailureWindow"/>, for
/// <see cref="MusicSyncConstants.AuthLockout"/>. Guessing a 256-bit token is hopeless anyway; this
/// stops a misbehaving device from hammering the PC and makes attempts visible in the log.
/// </summary>
public sealed class AuthFailureLimiter
{
    private readonly TimeProvider clock;
    private readonly Dictionary<IPAddress, Entry> entries = new();
    private readonly object gate = new();

    public AuthFailureLimiter(TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
    }

    /// <summary>Time left on the lockout, or null when the address may try.</summary>
    public TimeSpan? LockedFor(IPAddress address)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(Normalize(address), out var entry) || entry.LockedUntil is not { } until)
            {
                return null;
            }

            var left = until - clock.GetUtcNow();
            return left > TimeSpan.Zero ? left : null;
        }
    }

    /// <summary>Records a failed attempt; true when this attempt started a lockout.</summary>
    public bool RecordFailure(IPAddress address)
    {
        var now = clock.GetUtcNow();
        lock (gate)
        {
            Prune(now);
            var key = Normalize(address);
            if (!entries.TryGetValue(key, out var entry) || now - entry.WindowStart > MusicSyncConstants.AuthFailureWindow)
            {
                entry = new Entry { WindowStart = now };
                entries[key] = entry;
            }

            entry.Failures++;
            if (entry.Failures < MusicSyncConstants.MaxAuthFailures || entry.LockedUntil > now)
            {
                return false;
            }

            entry.LockedUntil = now + MusicSyncConstants.AuthLockout;
            return true;
        }
    }

    /// <summary>A correct token clears the address's history (e.g. the phone after re-pairing).</summary>
    public void RecordSuccess(IPAddress address)
    {
        lock (gate)
        {
            entries.Remove(Normalize(address));
        }
    }

    private void Prune(DateTimeOffset now)
    {
        // Bounded by distinct LAN addresses, but don't keep stale ones forever.
        if (entries.Count < 64)
        {
            return;
        }

        var stale = new List<IPAddress>();
        foreach (var (address, entry) in entries)
        {
            var expiry = entry.LockedUntil ?? entry.WindowStart + MusicSyncConstants.AuthFailureWindow;
            if (expiry < now)
            {
                stale.Add(address);
            }
        }

        stale.ForEach(address => entries.Remove(address));
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private sealed class Entry
    {
        public DateTimeOffset WindowStart { get; init; }

        public int Failures { get; set; }

        public DateTimeOffset? LockedUntil { get; set; }
    }
}
