using System;

namespace ScreenShotManager.MusicSync.Models;

/// <summary>
/// A phone that redeemed a pairing code. Only the SHA-256 of its token is stored, so the
/// devices file holds nothing that would let someone else sync.
/// </summary>
public sealed record PairedDevice(
    string Id,
    string Name,
    string TokenHash,
    DateTimeOffset PairedUtc,
    DateTimeOffset? LastSeenUtc);

/// <summary>One-time code shown in the pairing QR; the phone trades it for its own token.</summary>
public sealed record PairingCode(string Code, DateTimeOffset ExpiresUtc);
