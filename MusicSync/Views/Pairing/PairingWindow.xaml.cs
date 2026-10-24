using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QRCoder;
using ScreenShotManager.MusicSync.Models;
using MessageBox = System.Windows.MessageBox;

namespace ScreenShotManager.MusicSync.Views.Pairing;

/// <summary>
/// Shows the pairing QR code (host list, port, token, certificate fingerprint) and live library
/// size. "New pairing code" rotates the token, which unpairs every phone that has the old one.
/// </summary>
public partial class PairingWindow
{
    private readonly Func<PairingInfo> regenerate;
    private readonly Func<LibraryManifest?> currentManifest;
    private readonly DispatcherTimer libraryRefresh;
    private PairingInfo current;

    public PairingWindow(PairingInfo pairing, Func<PairingInfo> regenerate, Func<LibraryManifest?> currentManifest)
    {
        InitializeComponent();

        current = pairing;

        this.regenerate = regenerate ?? throw new ArgumentNullException(nameof(regenerate));
        this.currentManifest = currentManifest ?? throw new ArgumentNullException(nameof(currentManifest));

        ShowPairing(pairing);
        UpdateLibraryText();

        // The first scan of a big library can take a while; keep the count live while the window is open.
        libraryRefresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        libraryRefresh.Tick += (_, _) => UpdateLibraryText();
        libraryRefresh.Start();
        Closed += (_, _) => libraryRefresh.Stop();

        RegenerateButton.Click += OnRegenerateClick;
        CopyCodeButton.Click += OnCopyCodeClick;
    }

    private void ShowPairing(PairingInfo pairing)
    {
        current = pairing;
        EndpointText.Text = pairing.DisplayEndpoints;
        QrImage.Source = RenderQr(pairing.ToUri());
    }

    private void UpdateLibraryText()
    {
        var manifest = currentManifest();
        LibraryText.Text = manifest == null || manifest.GeneratedUtc == DateTimeOffset.MinValue
            ? "Scanning…"
            : $"{manifest.Tracks.Count} song(s)";
    }

    private void OnCopyCodeClick(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(current.ToUri());
            CopyCodeButton.Content = "Copied ✓";
        }
        catch (Exception ex)
        {
            // The clipboard can be briefly locked by another app.
            MessageBox.Show($"Could not copy: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnRegenerateClick(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Phones paired with the current code will stop syncing until they scan the new one. Continue?",
            "New pairing code",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            ShowPairing(regenerate());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to create a new pairing code: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
}
