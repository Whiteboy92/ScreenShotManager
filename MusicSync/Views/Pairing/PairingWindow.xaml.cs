using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QRCoder;
using ScreenShotManager.MusicSync.Models;
using ScreenShotManager.MusicSync.Services.Interfaces;
using MessageBox = System.Windows.MessageBox;

namespace ScreenShotManager.MusicSync.Views.Pairing;

/// <summary>
/// Shows a one-time pairing QR code (host list, port, code, certificate fingerprint), the live
/// library size and the paired phones. Codes expire after
/// <see cref="MusicSyncConstants.PairingCodeLifetime"/> and are replaced automatically while the
/// window is open; after a phone pairs, a fresh code is shown so the used one is never displayed.
/// </summary>
public partial class PairingWindow
{
    private static readonly TimeSpan ClipboardLifetime = TimeSpan.FromSeconds(60);

    private readonly Func<PairingInfo> newPairing;
    private readonly IDeviceRegistry devices;
    private readonly Func<LibraryManifest?> currentManifest;
    private readonly DispatcherTimer tick;
    private PairingInfo current;
    private string? copiedText;
    private DateTime copiedAtUtc;

    public PairingWindow(Func<PairingInfo> newPairing, IDeviceRegistry devices, Func<LibraryManifest?> currentManifest)
    {
        InitializeComponent();

        this.newPairing = newPairing ?? throw new ArgumentNullException(nameof(newPairing));
        this.devices = devices ?? throw new ArgumentNullException(nameof(devices));
        this.currentManifest = currentManifest ?? throw new ArgumentNullException(nameof(currentManifest));

        current = newPairing();
        ShowPairing(current);
        UpdateLibraryText();
        UpdateDevices();

        // Library count (first scan can take a while), code countdown and clipboard expiry.
        tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        tick.Tick += (_, _) => OnTick();
        tick.Start();

        EventHandler onDevicesChanged = (_, _) => Dispatcher.BeginInvoke(OnDevicesChanged);
        devices.Changed += onDevicesChanged;
        Closed += (_, _) =>
        {
            tick.Stop();
            devices.Changed -= onDevicesChanged;
            ClearCopiedCode();
        };

        RegenerateButton.Click += (_, _) => ShowPairing(newPairing());
        CopyCodeButton.Click += OnCopyCodeClick;
    }

    private void ShowPairing(PairingInfo pairing)
    {
        current = pairing;
        EndpointText.Text = pairing.DisplayEndpoints;
        QrImage.Source = RenderQr(pairing.ToUri());
        CopyCodeButton.Content = "Copy code";
        UpdateExpiry();
    }

    private void OnTick()
    {
        if (current.Code.ExpiresUtc <= DateTimeOffset.UtcNow)
        {
            ShowPairing(newPairing());
        }

        UpdateExpiry();
        if (DateTime.UtcNow - copiedAtUtc >= ClipboardLifetime)
        {
            ClearCopiedCode();
        }

        if (DateTime.UtcNow.Second % 2 == 0)
        {
            UpdateLibraryText();
        }
    }

    private void UpdateExpiry()
    {
        var left = current.Code.ExpiresUtc - DateTimeOffset.UtcNow;
        if (left < TimeSpan.Zero)
        {
            left = TimeSpan.Zero;
        }

        ExpiryText.Text = $"Code expires in {(int)left.TotalMinutes}:{left.Seconds:00}";
    }

    private void UpdateLibraryText()
    {
        var manifest = currentManifest();
        LibraryText.Text = manifest == null || manifest.GeneratedUtc == DateTimeOffset.MinValue
            ? "Scanning…"
            : $"{manifest.Tracks.Count} song(s)";
    }

    private void OnDevicesChanged()
    {
        var before = DeviceList.Items.Count;
        UpdateDevices();
        if (DeviceList.Items.Count > before)
        {
            // The shown code was just used; don't leave a spent code on screen.
            ShowPairing(newPairing());
            ExpiryText.Text = "Phone paired ✓  New code ready for the next one.";
        }
    }

    private void UpdateDevices()
    {
        var rows = devices.Devices
            .OrderBy(d => d.PairedUtc)
            .Select(d => new DeviceRow(d.Id, d.Name, Describe(d)))
            .ToList();
        DeviceList.ItemsSource = rows;
        NoDevicesText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string Describe(PairedDevice device)
    {
        var paired = $"Paired {device.PairedUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)}";
        return device.LastSeenUtc is { } seen
            ? $"{paired} · last sync {seen.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}"
            : paired;
    }

    private void OnRemoveDeviceClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id })
        {
            return;
        }

        var name = devices.Devices.FirstOrDefault(d => d.Id == id)?.Name ?? "this phone";
        var confirm = MessageBox.Show(
            $"{name} will stop syncing until it is paired again. Remove it?",
            "Remove phone",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            devices.Remove(id);
        }
    }

    /// <summary>
    /// Copies the pairing link while keeping it out of Windows clipboard history and cloud
    /// clipboard, and clears it after a minute if it's still there.
    /// </summary>
    private void OnCopyCodeClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = current.ToUri();
            var data = new System.Windows.DataObject();
            data.SetText(text);
            data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[] { 0 }));
            data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
            data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
            System.Windows.Clipboard.SetDataObject(data, copy: true);
            copiedText = text;
            copiedAtUtc = DateTime.UtcNow;
            CopyCodeButton.Content = "Copied ✓ (clears in 1 min)";
        }
        catch (Exception ex)
        {
            // The clipboard can be briefly locked by another app.
            MessageBox.Show($"Could not copy: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ClearCopiedCode()
    {
        if (copiedText == null)
        {
            return;
        }

        try
        {
            if (System.Windows.Clipboard.ContainsText() && System.Windows.Clipboard.GetText() == copiedText)
            {
                System.Windows.Clipboard.Clear();
            }
        }
        catch
        {
            // Locked by another app; the code expires on its own anyway.
        }

        copiedText = null;
        CopyCodeButton.Content = "Copy code";
    }

    private static BitmapImage RenderQr(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(10);

        var image = new BitmapImage();
        using (var stream = new MemoryStream(png))
        {
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
        }

        image.Freeze();
        return image;
    }

    public sealed record DeviceRow(string Id, string Name, string Detail);
}
