using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ScreenShotManager.Helpers;
using ScreenShotManager.Models.Downscaling;
using ScreenShotManager.Services.Downscaling;
using ScreenShotManager.Services.Interfaces;
using ScreenShotManager.ViewModels;

namespace ScreenShotManager;

/// <summary>
/// "Downscale Video" feature wiring: single-instance arbitration, named-pipe IPC, the
/// background downscale queue and Explorer context-menu registration. Kept in a partial
/// file so the existing screenshot lifecycle in App.xaml.cs stays untouched.
/// </summary>
public partial class App
{
    private Mutex? singleInstanceMutex;
    private ServiceProvider? downscaleProvider;
    private IDownscaleService? downscaleService;
    private IDownscaleIpcServer? downscaleIpcServer;
    private DownscaleViewModel? downscaleViewModel;

    /// <summary>Exposed for a future activity window to data-bind against.</summary>
    public DownscaleViewModel? DownscaleViewModel => downscaleViewModel;

    private sealed record StartupArgs(bool IsDownscale, IReadOnlyList<string> Files, bool OpenSettings);

    private static StartupArgs ParseArgs(string[] args)
    {
        var files = new List<string>();
        var isDownscale = false;
        var openSettings = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, DownscaleConstants.CliArgument, StringComparison.OrdinalIgnoreCase))
            {
                isDownscale = true;
                // Everything after --downscale is treated as a file path (Explorer passes one %1).
                for (var j = i + 1; j < args.Length; j++)
                {
                    files.Add(args[j]);
                }

                break;
            }

            if (string.Equals(arg, "/settings", StringComparison.OrdinalIgnoreCase))
            {
                openSettings = true;
            }
        }

        return new StartupArgs(isDownscale, files, openSettings);
    }

    /// <summary>
    /// Returns true when this process should continue as the primary (tray) instance.
    /// When false, the call has already handled forwarding/exit for a secondary launch.
    /// </summary>
    private bool TryBecomePrimaryOrForward(StartupArgs parsed)
    {
        singleInstanceMutex = new Mutex(initiallyOwned: true, DownscaleConstants.SingleInstanceMutexName, out var createdNew);

        if (createdNew)
        {
            return true;
        }

        // Another instance already owns the tray. Forward any files over the pipe, then exit.
        if (parsed.IsDownscale && parsed.Files.Count > 0)
        {
            ForwardToPrimary(parsed.Files);
        }

        Shutdown(0);
        return false;
    }

    private static void ForwardToPrimary(IReadOnlyList<string> files)
    {
        try
        {
            var client = new DownscaleIpcClient();
            // Block briefly; this is a transient launcher process with no UI.
            var sent = client.TrySendAsync(files).GetAwaiter().GetResult();
            CaptureLog.Write($"[downscale] forwarded {files.Count} path(s) to primary: sent={sent}");
        }
        catch (Exception ex)
        {
            CaptureLog.Write($"[downscale] forward failed: {ex.Message}");
        }
    }

    private void InitializeDownscaleFeature(StartupArgs parsed)
    {
        try
        {
            var services = new ServiceCollection();
            services.AddDownscaleFeature();
            downscaleProvider = services.BuildServiceProvider();

            downscaleService = downscaleProvider.GetRequiredService<IDownscaleService>();
            downscaleIpcServer = downscaleProvider.GetRequiredService<IDownscaleIpcServer>();
            downscaleViewModel = downscaleProvider.GetRequiredService<DownscaleViewModel>();
            var contextMenu = downscaleProvider.GetRequiredService<IContextMenuService>();
            var tools = downscaleProvider.GetRequiredService<IToolPathProvider>();

            if (!tools.ToolsAvailable)
            {
                CaptureLog.Write(
                    $"[downscale] WARNING tools missing: {tools.FFmpegPath} / {tools.FFprobePath}");
            }

            // Ensure the Explorer right-click entry exists (idempotent, per-user).
            if (!contextMenu.IsRegistered())
            {
                contextMenu.Register();
            }

            downscaleService.JobFinished += OnDownscaleJobFinished;
            downscaleService.Start();

            downscaleIpcServer.PathReceived += (_, path) => downscaleService.Enqueue(path);
            downscaleIpcServer.Start();

            // Files passed to this very launch (we are the first instance).
            foreach (var file in parsed.Files)
            {
                downscaleService.Enqueue(file);
            }
        }
        catch (Exception ex)
        {
            CaptureLog.Write($"[downscale] feature init failed: {ex}");
        }
    }

    private void OnDownscaleJobFinished(object? sender, DownscaleJobResult result)
    {
        var fileName = System.IO.Path.GetFileName(result.SourcePath);
        var (title, message) = result.Outcome switch
        {
            DownscaleOutcome.Completed =>
                ("Downscale complete", $"{fileName} → {result.TargetResolution}"),
            DownscaleOutcome.Skipped =>
                ("Downscale skipped", $"{fileName}: {result.Message}"),
            _ => ("Downscale failed", $"{fileName}: {result.Message}"),
        };

        // Tray service touches WinForms UI; marshal onto the UI thread.
        Dispatcher.BeginInvoke(new Action(() =>
            systemTrayService?.ShowNotification(title, message)));
    }

    private void CleanupDownscaleFeature()
    {
        try { downscaleIpcServer?.Dispose(); } catch { /* ignore */ }
        try { downscaleService?.Stop(); } catch { /* ignore */ }
        try { downscaleViewModel?.Dispose(); } catch { /* ignore */ }
        try { downscaleProvider?.Dispose(); } catch { /* ignore */ }

        try
        {
            singleInstanceMutex?.ReleaseMutex();
            singleInstanceMutex?.Dispose();
        }
        catch { /* ignore */ }
    }
}
