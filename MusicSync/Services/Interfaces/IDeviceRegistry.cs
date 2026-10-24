using System;
using System.Collections.Generic;
using ScreenShotManager.MusicSync.Models;

namespace ScreenShotManager.MusicSync.Services.Interfaces;

/// <summary>
/// Phones allowed to sync, each with its own token, so one can be revoked without touching the
/// others. Pairing goes through a short-lived one-time code (see <see cref="IssuePairingCode"/>).
/// </summary>
public interface IDeviceRegistry
{
    IReadOnlyList<PairedDevice> Devices { get; }

    /// <summary>Raised (on a worker thread) when a device is added or removed.</summary>
    event EventHandler? Changed;

    /// <summary>The device a bearer token belongs to, or null. Also records when it was last seen.</summary>
    PairedDevice? Authenticate(string? presentedToken);

    /// <summary>
    /// Replaces the current one-time pairing code with a new one valid for
    /// <see cref="MusicSyncConstants.PairingCodeLifetime"/>. Only one code is valid at a time.
    /// </summary>
    PairingCode IssuePairingCode();

    /// <summary>Spends the pairing code and registers a device; its new token, or null if the code is wrong, used or expired.</summary>
    string? Redeem(string? code, string deviceName);

    /// <summary>Keeps a phone paired before per-device tokens existed working with its old token.</summary>
    void ImportLegacyToken(string token, string name);

    bool Remove(string deviceId);
}
