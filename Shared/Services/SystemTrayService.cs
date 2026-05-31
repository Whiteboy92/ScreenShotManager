using System;
using System.Windows.Forms;
using ScreenShotManager.Shared.Helpers;
using ScreenShotManager.Shared.Services.Interfaces;

namespace ScreenShotManager.Shared.Services;

/// <summary>
/// Service for managing system tray icon and interactions
/// </summary>
public class SystemTrayService : ISystemTrayService
{
    private NotifyIcon? notifyIcon;

    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? OpenSaveFolderRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<bool>? PauseToggled;
    public event EventHandler? TakeScreenshotRequested;

    public void Initialize()
    {
        notifyIcon = new NotifyIcon
        {
            Icon = IconHelper.CreateTrayIcon(),
            Visible = true,
            Text = "ScreenShotManager - Screenshot Tool",
        };

        var contextMenu = CreateContextMenu();
        notifyIcon.ContextMenuStrip = contextMenu;
        notifyIcon.DoubleClick += (_, _) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var contextMenu = new ContextMenuStrip();
        
        contextMenu.Items.Add("📸 Take Screenshot", null, 
            (_, _) => TakeScreenshotRequested?.Invoke(this, EventArgs.Empty));
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Open Settings", null, 
            (_, _) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));
        contextMenu.Items.Add("Open Save Folder", null, 
            (_, _) => OpenSaveFolderRequested?.Invoke(this, EventArgs.Empty));
        contextMenu.Items.Add(new ToolStripSeparator());
        
        var pauseItem = CreatePauseMenuItem();
        contextMenu.Items.Add(pauseItem);
        
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, 
            (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        return contextMenu;
    }

    private ToolStripMenuItem CreatePauseMenuItem()
    {
        var pauseItem = new ToolStripMenuItem("Pause Hotkey")
        {
            CheckOnClick = true,
        };
        pauseItem.CheckedChanged += (_, _) => 
            PauseToggled?.Invoke(this, pauseItem.Checked);
        return pauseItem;
    }

    public void ShowNotification(string title, string message, NotificationSeverity severity = NotificationSeverity.Info)
    {
        var icon = severity switch
        {
            NotificationSeverity.Error => ToolTipIcon.Error,
            NotificationSeverity.Warning => ToolTipIcon.Warning,
            _ => ToolTipIcon.Info,
        };

        // Errors/warnings linger longer so they aren't missed; info auto-dismisses quickly.
        var timeoutMs = severity == NotificationSeverity.Info ? 2000 : 5000;
        notifyIcon?.ShowBalloonTip(timeoutMs, title, message, icon);
    }

    public void Dispose()
    {
        if (notifyIcon != null)
        {
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            notifyIcon = null;
        }
        GC.SuppressFinalize(this);
    }
}

