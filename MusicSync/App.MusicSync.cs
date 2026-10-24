using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ScreenShotManager.MusicSync.Models;
using ScreenShotManager.MusicSync.Services;
using ScreenShotManager.MusicSync.Services.Interfaces;
using ScreenShotManager.MusicSync.Views.Pairing;
using ScreenShotManager.Shared.Helpers;
using ScreenShotManager.Shared.Services;

namespace ScreenShotManager;

/// <summary>
/// "Music Sync" feature wiring: library watcher, in-process HTTPS API, mDNS announcement and the
/// pairing window. Kept in a partial file like the downscale feature so App.xaml.cs stays small.
/// </summary>
public partial class App
{
    private ServiceProvider? musicSyncProvider;
    private IMusicLibraryService? musicLibrary;
    private IMusicSyncServer? musicServer;
    private IMdnsAdvertiser? musicAdvertiser;
    private ICertificateProvider? musicCertificates;
    private IDeviceRegistry? musicDevices;
    private PairingWindow? pairingWindow;

    /// <summary>Settings the server was last (re)started with; used to skip no-op restarts.</summary>
    private (bool Enabled, string Folder, int Port)? appliedMusicSync;

    /// <summary>Start/stop transitions run off the UI thread, chained so they never overlap.</summary>
    private Task musicSyncTransition = Task.CompletedTask;

    private void InitializeMusicSyncFeature()
    {
        try
        {
            var services = new ServiceCollection();
            services.AddMusicSyncFeature();
            musicSyncProvider = services.BuildServiceProvider();

            musicLibrary = musicSyncProvider.GetRequiredService<IMusicLibraryService>();
            musicServer = musicSyncProvider.GetRequiredService<IMusicSyncServer>();
            musicAdvertiser = musicSyncProvider.GetRequiredService<IMdnsAdvertiser>();
            musicCertificates = musicSyncProvider.GetRequiredService<ICertificateProvider>();
            musicDevices = musicSyncProvider.GetRequiredService<IDeviceRegistry>();
            MigrateLegacyMusicSyncToken();

            ApplyMusicSyncSettings();
        }
        catch (Exception ex)
        {
            CaptureLog.Write($"[musicsync] feature init failed: {ex}");
            ShowError($"Music Sync failed to start: {ex.Message}");
        }
    }

    /// <summary>Starts, stops or restarts the server to match the current settings. UI thread.</summary>
    private void ApplyMusicSyncSettings()
    {
        if (musicServer == null)
        {
            return;
        }

        // Cheap to set every time; no restart needed when only the backup folder changes.
        musicServer.BackupFolder = settings.GetMusicBackupFolder();

        var desired = (settings.MusicSyncEnabled, settings.MusicFolder, settings.MusicSyncPort);
        if (appliedMusicSync == desired)
        {
            return;
        }

        appliedMusicSync = desired;
        musicSyncTransition = musicSyncTransition
            .ContinueWith(_ => RestartMusicSyncAsync(desired.MusicSyncEnabled, desired.MusicFolder, desired.MusicSyncPort),
                TaskScheduler.Default)
            .Unwrap();
    }

    /// <summary>
    /// The phone paired before per-device tokens keeps working: its shared token becomes one
    /// device entry (stored hashed) and is removed from config.json.
    /// </summary>
    private void MigrateLegacyMusicSyncToken()
    {
        if (string.IsNullOrEmpty(settings.MusicSyncToken) || musicDevices == null)
        {
            return;
        }

        musicDevices.ImportLegacyToken(settings.MusicSyncToken, "Phone (paired earlier)");
        settings.MusicSyncToken = string.Empty;
        try
        {
            settingsService?.Save(settings);
            CaptureLog.Write("[musicsync] moved legacy pairing token into the devices list");
        }
        catch (Exception ex)
        {
            CaptureLog.Write($"[musicsync] failed to clear legacy token: {ex.Message}");
        }
    }

    private async Task RestartMusicSyncAsync(bool enabled, string folder, int port)
    {
        await StopMusicSyncAsync().ConfigureAwait(false);

        if (!enabled || musicServer == null || musicLibrary == null || musicCertificates == null)
        {
            return;
        }

        try
        {
            var certificate = musicCertificates.GetOrCreate();
            musicLibrary.Start(folder);
            await musicServer.StartAsync(port, certificate).ConfigureAwait(false);
            musicAdvertiser?.Start(musicServer.BoundPort, musicCertificates.GetFingerprint());
            CaptureLog.Write($"[musicsync] serving {folder} on port {musicServer.BoundPort}");
        }
        catch (Exception ex)
        {
            CaptureLog.Write($"[musicsync] start failed: {ex}");
            await StopMusicSyncAsync().ConfigureAwait(false);
            Notify(
                "Music Sync unavailable",
                $"Could not listen on port {port}: {ex.Message}",
                NotificationSeverity.Error);
        }
    }

    private async Task StopMusicSyncAsync()
    {
        try { musicAdvertiser?.Stop(); } catch { /* ignore */ }
        try { musicLibrary?.Stop(); } catch { /* ignore */ }

        if (musicServer != null)
        {
            try { await musicServer.StopAsync().ConfigureAwait(false); }
            catch (Exception ex) { CaptureLog.Write($"[musicsync] stop failed: {ex.Message}"); }
        }
    }

    private void OpenPairingWindow()
    {
        try
        {
            if (musicServer == null || musicCertificates == null)
            {
                ShowError("Music Sync is not initialized.");
                return;
            }

            if (!settings.MusicSyncEnabled || !musicServer.IsRunning)
            {
                Notify(
                    "Music Sync is off",
                    "Turn on Music Sync in Settings, then pair your phone.",
                    NotificationSeverity.Warning);
                return;
            }

            if (pairingWindow != null)
            {
                pairingWindow.Activate();
                return;
            }

            pairingWindow = new PairingWindow(NewPairingInfo, musicDevices!, () => musicLibrary?.Current);
            pairingWindow.Closed += (_, _) => pairingWindow = null;
            pairingWindow.Show();
        }
        catch (Exception ex)
        {
            ShowError($"Failed to open pairing window: {ex.Message}");
        }
    }

    /// <summary>Fresh one-time code (replacing any unused one) plus how to reach this PC.</summary>
    private PairingInfo NewPairingInfo() => new(
        Environment.MachineName,
        LanAddressProvider.GetLanAddresses(),
        musicServer!.BoundPort,
        musicDevices!.IssuePairingCode(),
        musicCertificates!.GetFingerprint());

    private void CleanupMusicSyncFeature()
    {
        // Called twice on exit (ExitApplication + Application_Exit); the second pass is a no-op.
        if (musicSyncProvider == null)
        {
            return;
        }

        try { pairingWindow?.Close(); } catch { /* ignore */ }

        try
        {
            // Off the UI thread: Kestrel shutdown must not wait on the dispatcher we're blocking.
            Task.Run(async () =>
            {
                await musicSyncTransition.ConfigureAwait(false);
                await StopMusicSyncAsync().ConfigureAwait(false);
            }).Wait(TimeSpan.FromSeconds(5));
        }
        catch { /* ignore */ }

        try { musicSyncProvider.Dispose(); } catch { /* ignore */ }
        musicSyncProvider = null;
    }
}
