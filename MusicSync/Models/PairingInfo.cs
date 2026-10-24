using System;
using System.Collections.Generic;
using System.Linq;

namespace ScreenShotManager.MusicSync.Models;

/// <summary>
/// Everything the phone needs to talk to this PC, encoded into the pairing QR code as
/// <c>musicsync://pair?v=1&amp;name=..&amp;hosts=ip1,ip2&amp;port=..&amp;token=..&amp;fp=..</c>.
/// Several hosts are listed because a PC can have more than one LAN address (Ethernet + Wi-Fi);
/// <c>fp</c> is the SHA-256 fingerprint of the self-signed TLS certificate the phone pins.
/// </summary>
public sealed record PairingInfo(
    string Name,
    IReadOnlyList<string> Hosts,
    int Port,
    string Token,
    string CertificateFingerprint)
{
    public string ToUri()
    {
        var query = string.Join("&", new[]
        {
            $"v={MusicSyncConstants.ApiVersion}",
            $"name={Uri.EscapeDataString(Name)}",
            $"hosts={Uri.EscapeDataString(string.Join(",", Hosts))}",
            $"port={Port}",
            $"token={Uri.EscapeDataString(Token)}",
            $"fp={Uri.EscapeDataString(CertificateFingerprint)}",
        });

        return $"{MusicSyncConstants.PairingScheme}://pair?{query}";
    }

    /// <summary>Human-readable endpoint list for the pairing window ("192.168.1.5:47821").</summary>
    public string DisplayEndpoints => Hosts.Count == 0
        ? "(no LAN address found)"
        : string.Join(", ", Hosts.Select(h => $"{h}:{Port}"));
}
