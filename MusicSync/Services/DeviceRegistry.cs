using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ScreenShotManager.MusicSync.Models;
using ScreenShotManager.MusicSync.Services.Interfaces;
using ScreenShotManager.Shared.Helpers;

namespace ScreenShotManager.MusicSync.Services;

/// <summary>
/// JSON-backed <see cref="IDeviceRegistry"/> at %AppData%\ScreenShotManager\musicsync-devices.json.
/// Tokens are 256-bit random values; only their SHA-256 is kept, compared in constant time.
/// "Last seen" is persisted at most every <see cref="LastSeenPersistInterval"/> to avoid a disk
/// write per request.
/// </summary>
public sealed class DeviceRegistry : IDeviceRegistry
{
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ScreenShotManager",
        "musicsync-devices.json");

    private static readonly TimeSpan LastSeenPersistInterval = TimeSpan.FromMinutes(10);

    private readonly object sync = new();
    private readonly string path;
    private readonly TimeProvider clock;
    private readonly List<PairedDevice> devices;
    private PairingCode? pendingCode;

    public DeviceRegistry()
        : this(DefaultPath, TimeProvider.System)
    {
    }

    internal DeviceRegistry(string path, TimeProvider clock)
    {
        this.path = path;
        this.clock = clock;
        devices = Load();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<PairedDevice> Devices
    {
        get
        {
            lock (sync)
            {
                return devices.ToList();
            }
        }
    }

    public PairedDevice? Authenticate(string? presentedToken)
    {
        if (string.IsNullOrEmpty(presentedToken))
        {
            return null;
        }

        var presented = SHA256.HashData(Encoding.UTF8.GetBytes(presentedToken));
        var now = clock.GetUtcNow();
        lock (sync)
        {
            var index = -1;
            // Check every device (no early exit) so timing doesn't reveal which one nearly matched.
            for (var i = 0; i < devices.Count; i++)
            {
                if (CryptographicOperations.FixedTimeEquals(presented, Convert.FromHexString(devices[i].TokenHash)))
                {
                    index = i;
                }
            }

            if (index < 0)
            {
                return null;
            }

            var match = devices[index];
            if (match.LastSeenUtc is not { } seen || now - seen > LastSeenPersistInterval)
            {
                match = match with { LastSeenUtc = now };
                devices[index] = match;
                Save();
            }

            return match;
        }
    }

    public PairingCode IssuePairingCode()
    {
        var code = new PairingCode(
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            clock.GetUtcNow() + MusicSyncConstants.PairingCodeLifetime);
        lock (sync)
        {
            pendingCode = code;
        }

        return code;
    }

    public string? Redeem(string? code, string deviceName)
    {
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        string token;
        lock (sync)
        {
            if (pendingCode is not { } pending
                || pending.ExpiresUtc <= clock.GetUtcNow()
                || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(code), Encoding.UTF8.GetBytes(pending.Code)))
            {
                return null;
            }

            pendingCode = null; // one use only
            token = PairingTokens.Generate();
            devices.Add(NewDevice(CleanName(deviceName), token));
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return token;
    }

    public void ImportLegacyToken(string token, string name)
    {
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        lock (sync)
        {
            var hash = Hash(token);
            if (devices.Any(d => d.TokenHash == hash))
            {
                return;
            }

            devices.Add(NewDevice(name, token));
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Remove(string deviceId)
    {
        lock (sync)
        {
            if (devices.RemoveAll(d => d.Id == deviceId) == 0)
            {
                return false;
            }

            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private PairedDevice NewDevice(string name, string token) => new(
        Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
        name,
        Hash(token),
        clock.GetUtcNow(),
        null);

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>The name comes from the phone; keep it short and printable for the device list.</summary>
    internal static string CleanName(string? name)
    {
        var cleaned = new string((name ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length > 40)
        {
            cleaned = cleaned[..40];
        }

        return cleaned.Length == 0 ? "Phone" : cleaned;
    }

    /// <summary>Callers hold <see cref="sync"/>.</summary>
    private void Save()
    {
        try
        {
            FileHelper.EnsureDirectoryExists(path);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(devices));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            CaptureLog.Write($"[musicsync] devices save failed: {ex.Message}");
        }
    }

    private List<PairedDevice> Load()
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<List<PairedDevice>>(File.ReadAllText(path)) ?? new List<PairedDevice>();
            }
        }
        catch (Exception ex)
        {
            CaptureLog.Write($"[musicsync] devices file unreadable, no phones paired: {ex.Message}");
        }

        return new List<PairedDevice>();
    }
}
