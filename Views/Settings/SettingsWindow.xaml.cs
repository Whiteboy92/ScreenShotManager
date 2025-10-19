using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Forms;
using ScreenShotManager.Models;
using ScreenShotManager.Services.Interfaces;
using MessageBox = System.Windows.MessageBox;

namespace ScreenShotManager.Views.Settings;

public partial class SettingsWindow
{
    private readonly ISettingsService settingsService;
    private readonly AppSettings settings;

    public SettingsWindow(ISettingsService settingsService, AppSettings settings)
    {
        try
        {
            InitializeComponent();
            
            this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            
            LoadSettings();
            SetupEventHandlers();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize settings window: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    private void SetupEventHandlers()
    {
        BrowseFolderButton.Click += OnBrowseFolderClick;
        FilenamePatternTextBox.TextChanged += OnFilenamePatternChanged;
        MaxHistorySlider.ValueChanged += OnMaxHistoryChanged;
        DisableDimmingCheckBox.Checked += (_, _) => SaveDisableDimming(true);
        DisableDimmingCheckBox.Unchecked += (_, _) => SaveDisableDimming(false);
        StartOnLoginCheckBox.Checked += OnStartOnLoginChecked;
        StartOnLoginCheckBox.Unchecked += OnStartOnLoginUnchecked;
    }

    private void ScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        try
        {
            var scrollViewer = sender as System.Windows.Controls.ScrollViewer;
            if (scrollViewer == null) return;

            double scrollAmount = e.Delta / 3.0;
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - scrollAmount);
            
            e.Handled = true;
        }
        catch
        {
            // Silently handle scroll errors
        }
    }

    private void LoadSettings()
    {
        try
        {
            SaveFolderTextBox.Text = settings.SaveFolder;
            
            FileFormatDropdown.SelectedValue = settings.FileFormat.ToUpper();

            FilenamePatternTextBox.Text = settings.FilenamePattern;
            DisableDimmingCheckBox.IsChecked = settings.DisableDimming;
            StartOnLoginCheckBox.IsChecked = settings.StartOnLogin;
            MaxHistorySlider.Value = settings.MaxAutoSaveHistory;
            
            UpdateMaxHistoryText();
            UpdateFilenamePreview();
        }
        catch (Exception ex)
        {
            ShowError($"Error loading settings: {ex.Message}");
        }
    }

    private void UpdateMaxHistoryText()
    {
        MaxHistoryText.Text = ((int)MaxHistorySlider.Value).ToString();
    }

    private void UpdateFilenamePreview()
    {
        var pattern = FilenamePatternTextBox.Text;
        var example = pattern
            .Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd"))
            .Replace("{time}", DateTime.Now.ToString("HH-mm-ss"));
        
        var format = FileFormatDropdown.SelectedValue.ToLower();

        FilenamePreview.Text = $"Example: {example}.{format}";
    }

    private void OnFilenamePatternChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        try
        {
            UpdateFilenamePreview();
            settings.FilenamePattern = FilenamePatternTextBox.Text;
            settingsService.Save(settings);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to save filename pattern: {ex.Message}");
        }
    }

    private void FileFormatDropdown_SelectionChanged(object? sender, string selectedFormat)
    {
        try
        {
            UpdateFilenamePreview();
            settings.FileFormat = selectedFormat.ToUpper();
            settingsService.Save(settings);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to save file format: {ex.Message}");
        }
    }

    private void OnMaxHistoryChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        try
        {
            UpdateMaxHistoryText();
            settings.MaxAutoSaveHistory = (int)MaxHistorySlider.Value;
            settingsService.Save(settings);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to save max history: {ex.Message}");
        }
    }

    private void SaveDisableDimming(bool isDisabled)
    {
        try
        {
            settings.DisableDimming = isDisabled;
            settingsService.Save(settings);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to save dimming setting: {ex.Message}");
        }
    }

    private void OnBrowseFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new FolderBrowserDialog
            {
                Description = "Select default save folder for screenshots",
                SelectedPath = settings.SaveFolder,
                ShowNewFolderButton = true,
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && 
                !string.IsNullOrEmpty(dialog.SelectedPath))
            {
                SaveFolderTextBox.Text = dialog.SelectedPath;
                
                settings.SaveFolder = dialog.SelectedPath;
                settingsService.Save(settings);
            }
        }
        catch (Exception ex)
        {
            ShowError($"Error opening folder browser: {ex.Message}");
        }
    }


    private void OnStartOnLoginChecked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!IsRunningAsAdministrator())
            {
                StartOnLoginCheckBox.IsChecked = false;
                
                RestartAsAdministrator();
                e.Handled = true;
                return;
            }
            
            settings.StartOnLogin = true;
            settingsService.SetStartupRegistry(true);
            settingsService.Save(settings);
        }
        catch (Exception ex)
        {
            StartOnLoginCheckBox.IsChecked = false;
            ShowError($"Error enabling startup option: {ex.Message}");
        }
    }
    
    private void OnStartOnLoginUnchecked(object sender, RoutedEventArgs e)
    {
        try
        {
            settings.StartOnLogin = false;
            settingsService.SetStartupRegistry(false);
            settingsService.Save(settings);
        }
        catch (Exception ex)
        {
            ShowError($"Error disabling startup option: {ex.Message}");
        }
    }
    
    private bool IsRunningAsAdministrator()
    {
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
    
    private void RestartAsAdministrator()
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = Process.GetCurrentProcess().MainModule?.FileName ?? "ScreenShotManager.exe",
                UseShellExecute = true,
                Verb = "runas",
                Arguments = "/settings",
            };
            
            Process.Start(processInfo);
            
            System.Windows.Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                "Administrator privileges were not granted. The 'Start on Windows login' option cannot be enabled.",
                "Access Denied",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to restart as administrator: {ex.Message}");
        }
    }

    private void ShowError(string message)
    {
        MessageBox.Show(message, "Error",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

