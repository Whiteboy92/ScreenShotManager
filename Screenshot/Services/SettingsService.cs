using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;
using ScreenShotManager.Shared.Helpers;
using ScreenShotManager.Screenshot.Models;
using ScreenShotManager.Screenshot.Services.Interfaces;

namespace ScreenShotManager.Screenshot.Services;

/// <summary>
/// Service for managing application settings
/// </summary>
public class SettingsService : ISettingsService
{
    private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ScreenShotManager";
    
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ScreenShotManager",
        "config.json"
    );

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            FileHelper.EnsureDirectoryExists(ConfigPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
            };
            var json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save settings: {ex.Message}", ex);
        }
    }

    public void SetStartupRegistry(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
            if (key != null)
            {
                if (enable)
                {
                    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (exePath != null)
                    {
                        key.SetValue(AppName, exePath);
                    }
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
        }
        catch (Exception ex)
        {
            throw new UnauthorizedAccessException(
                "Failed to update startup settings. Administrator privileges may be required.", ex);
        }
    }

    public void CleanupOldFiles(AppSettings settings)
    {
        if (settings.MaxAutoSaveHistory <= 0)
            return;

        try
        {
            var directory = new DirectoryInfo(settings.SaveFolder);
            if (!directory.Exists)
                return;

            var files = directory.GetFiles("*.*")
                .Where(f => FileHelper.IsImageFile(f.Extension))
                .OrderByDescending(f => f.CreationTime)
                .ToList();

            if (files.Count > settings.MaxAutoSaveHistory)
            {
                foreach (var file in files.Skip(settings.MaxAutoSaveHistory))
                {
                    FileHelper.TryDeleteFile(file.FullName);
                }
            }
        }
        catch
        {

        }
    }
}

