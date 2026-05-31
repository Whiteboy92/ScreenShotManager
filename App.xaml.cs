using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ScreenShotManager.Screenshot.Models;
using ScreenShotManager.Screenshot.Services;
using ScreenShotManager.Screenshot.Services.Interfaces;
using ScreenShotManager.Screenshot.Views.Overlay;
using ScreenShotManager.Screenshot.Views.Settings;
using ScreenShotManager.Shared.Services;
using ScreenShotManager.Shared.Services.Interfaces;
using ScreenShotManager.VolumeManager.Services;
using ScreenShotManager.VolumeManager.Services.Interfaces;
using Bitmap = System.Drawing.Bitmap;
using MessageBox = System.Windows.MessageBox;

namespace ScreenShotManager;

public partial class App
{
    // Services
    private IKeyboardHookService? keyboardHookService;
    private ISystemTrayService? systemTrayService;
    private IScreenCaptureService? screenCaptureService;
    private ISettingsService? settingsService;
    private IVolumeMonitorService? inputVolumeMonitorService;
    
    // State
    private AppSettings settings = new();
    private CancellationTokenSource? appCancellationTokenSource;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var parsed = ParseArgs(e.Args);

        // Single-instance gate. A secondary launch (e.g. an Explorer "Downscale Video" click while
        // the tray app is already running) forwards its file path over the pipe and exits here.
        if (!TryBecomePrimaryOrForward(parsed))
        {
            return;
        }

        // Create cancellation token for the entire app lifetime
        appCancellationTokenSource = new CancellationTokenSource();

        InitializeServices();
        LoadSettings();
        SetupEventHandlers();
        StartServices();

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Downscale feature: IPC server, background queue and context-menu registration.
        InitializeDownscaleFeature(parsed);

        if (parsed.OpenSettings)
        {
            Dispatcher.BeginInvoke(new Action(OpenSettings));
        }
    }

    private void InitializeServices()
    {
        try
        {
            settingsService = new SettingsService();
            screenCaptureService = new ScreenCaptureService();
            keyboardHookService = new KeyboardHookService();
            systemTrayService = new SystemTrayService();
            inputVolumeMonitorService = new InputVolumeMonitorService();
        }
        catch (Exception ex)
        {
            ShowError($"Failed to initialize services: {ex.Message}\n\nThe application will now exit.");
            Shutdown();
        }
    }

    private void LoadSettings()
    {
        try
        {
            settings = settingsService!.Load();
        }
        catch (Exception ex)
        {
            ShowError($"Failed to load settings: {ex.Message}\n\nUsing default settings.");
            settings = new AppSettings();
        }
    }

    private void SetupEventHandlers()
    {
        try
        {
            if (keyboardHookService != null)
            {
                keyboardHookService.PrintScreenPressed += OnPrintScreenPressed;
            }

            if (systemTrayService != null)
            {
                systemTrayService.TakeScreenshotRequested += (_, _) => HandleScreenCapture();
                systemTrayService.OpenSettingsRequested += (_, _) => OpenSettings();
                systemTrayService.OpenSaveFolderRequested += (_, _) => OpenSaveFolder();
                systemTrayService.ExitRequested += (_, _) => ExitApplication();
                systemTrayService.PauseToggled += OnPauseToggled;
            }
        }
        catch (Exception ex)
        {
            ShowError($"Failed to setup event handlers: {ex.Message}");
        }
    }

    private void StartServices()
    {
        try
        {
            systemTrayService?.Initialize();
            keyboardHookService?.Start();
            
            if (inputVolumeMonitorService != null && appCancellationTokenSource != null)
            {
                _ = inputVolumeMonitorService.StartMonitoringAsync(appCancellationTokenSource.Token);
            }
        }
        catch (Exception ex)
        {
            ShowError($"Failed to start services: {ex.Message}\n\nSome features may not work correctly.");
        }
    }

    private void OnPauseToggled(object? sender, bool isPaused)
    {
        try
        {
            if (keyboardHookService != null)
            {
                keyboardHookService.IsPaused = isPaused;
            }
        }
        catch (Exception ex)
        {
            ShowError($"Failed to toggle pause: {ex.Message}");
        }
    }

    private void OnPrintScreenPressed(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(HandleScreenCapture);
    }

    private void HandleScreenCapture()
    {
        try
        {
            if (screenCaptureService == null)
            {
                ShowError("Screen capture service is not initialized.");
                return;
            }

            var overlay = new OverlayWindow(screenCaptureService, settings);
            var result = overlay.ShowDialog();

            if (result == true && overlay.CapturedBitmap != null)
            {
                ProcessCapturedScreenshot(overlay.CapturedBitmap);
            }
        }
        catch (Exception ex)
        {
            ShowError($"Error capturing screenshot: {ex.Message}\n\nStack trace:\n{ex.StackTrace}");
        }
    }

    private async void ProcessCapturedScreenshot(Bitmap bitmap)
    {
        try
        {
            if (screenCaptureService == null)
            {
                ShowError("Screen capture service is not initialized.");
                bitmap.Dispose();
                return;
            }

            var fileName = settings.GetFileName();
            var filePath = Path.Combine(settings.SaveFolder, fileName);
            
            var copyTask = screenCaptureService.CopyToClipboardAsync(bitmap);
            var saveTask = screenCaptureService.SaveToFileAsync(bitmap, filePath, settings.FileFormat);
            
            await Task.WhenAll(copyTask, saveTask);

            bitmap.Dispose();

            systemTrayService?.ShowNotification(
                "Screenshot Captured",
                $"Saved to: {fileName}"
            );

            _ = Task.Run(() => settingsService?.CleanupOldFiles(settings));
        }
        catch (Exception ex)
        {
            bitmap.Dispose();
            ShowError($"Error processing screenshot: {ex.Message}\n\nFile: {settings.SaveFolder}\nDetails: {ex.InnerException?.Message ?? "None"}");
        }
    }

    private void OpenSettings()
    {
        try
        {
            if (settingsService == null)
            {
                ShowError("Settings service is not initialized.");
                return;
            }

            var settingsWindow = new SettingsWindow(settingsService, settings);
            if (settingsWindow.ShowDialog() == true)
            {
                LoadSettings();
            }
        }
        catch (Exception ex)
        {
            ShowError($"Error opening settings: {ex.Message}");
        }
    }

    private void OpenSaveFolder()
    {
        try
        {
            if (!Directory.Exists(settings.SaveFolder))
            {
                Directory.CreateDirectory(settings.SaveFolder);
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = settings.SaveFolder,
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to open save folder: {ex.Message}\n\nFolder: {settings.SaveFolder}\n\nPlease check if the folder exists.");
        }
    }

    private void ExitApplication()
    {
        try
        {
            CleanupServices();
            Shutdown();
        }
        catch
        {
            Shutdown();
        }
    }

    private void CleanupServices()
    {
        CleanupDownscaleFeature();

        try { keyboardHookService?.Dispose(); }
        catch { /* Silently dispose */ }

        try { systemTrayService?.Dispose(); } 
        catch { /* Silently dispose */ }
        
        try 
        { 
            inputVolumeMonitorService?.StopMonitoring();
            appCancellationTokenSource?.Cancel();
            appCancellationTokenSource?.Dispose();
        } 
        catch { /* Silently dispose */ }
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        CleanupServices();
    }

    private void ShowError(string message)
    {
        MessageBox.Show(message, "Error", 
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}