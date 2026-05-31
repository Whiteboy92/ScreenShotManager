using System;
using Microsoft.Win32;
using ScreenShotManager.Shared.Helpers;
using ScreenShotManager.VideoDownscaler.Models;
using ScreenShotManager.VideoDownscaler.Services.Interfaces;

namespace ScreenShotManager.VideoDownscaler.Services;

/// <summary>
/// Registers the "Downscale Video" verb under
/// <c>HKCU\Software\Classes\SystemFileAssociations\{ext}\shell\{verb}</c> for every supported
/// extension. Per-user (HKCU) so no administrator rights are required, and applies regardless
/// of which application owns the file's ProgID.
/// </summary>
public sealed class ContextMenuService : IContextMenuService
{
    private const string ClassesRoot = @"Software\Classes\SystemFileAssociations";

    private static string ExecutablePath =>
        Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;

    private static string CommandLine => $"\"{ExecutablePath}\" {DownscaleConstants.CliArgument} \"%1\"";

    public bool IsRegistered()
    {
        foreach (var ext in DownscaleConstants.SupportedExtensions)
        {
            using var command = Registry.CurrentUser.OpenSubKey(CommandKeyPath(ext));
            if (command?.GetValue(null) as string != CommandLine)
            {
                return false;
            }
        }

        return true;
    }

    public void Register()
    {
        try
        {
            foreach (var ext in DownscaleConstants.SupportedExtensions)
            {
                using var verb = Registry.CurrentUser.CreateSubKey(VerbKeyPath(ext));
                verb.SetValue(null, DownscaleConstants.ContextMenuLabel);
                verb.SetValue("Icon", $"\"{ExecutablePath}\",0");

                using var command = verb.CreateSubKey("command");
                command.SetValue(null, CommandLine);
            }

            CaptureLog.Write("[contextmenu] registered Downscale Video verb");
        }
        catch (Exception ex)
        {
            CaptureLog.Write($"[contextmenu] register failed: {ex.Message}");
        }
    }

    public void Unregister()
    {
        try
        {
            foreach (var ext in DownscaleConstants.SupportedExtensions)
            {
                Registry.CurrentUser.DeleteSubKeyTree(VerbKeyPath(ext), throwOnMissingSubKey: false);
            }

            CaptureLog.Write("[contextmenu] unregistered Downscale Video verb");
        }
        catch (Exception ex)
        {
            CaptureLog.Write($"[contextmenu] unregister failed: {ex.Message}");
        }
    }

    private static string VerbKeyPath(string ext) =>
        $@"{ClassesRoot}\{ext}\shell\{DownscaleConstants.ContextMenuVerb}";

    private static string CommandKeyPath(string ext) => $@"{VerbKeyPath(ext)}\command";
}
