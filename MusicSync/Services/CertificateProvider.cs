using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ScreenShotManager.MusicSync.Services.Interfaces;
using ScreenShotManager.Shared.Helpers;

namespace ScreenShotManager.MusicSync.Services;

/// <summary>
/// Self-signed ECDSA P-256 certificate persisted at %AppData%\ScreenShotManager\musicsync-cert.pfx.
/// Valid for 10 years; regenerated only when missing, unreadable or close to expiry (which forces
/// the phone to re-pair, since its pinned fingerprint changes).
/// </summary>
public sealed class CertificateProvider : ICertificateProvider
{
    private static readonly string DefaultPfxPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ScreenShotManager",
        "musicsync-cert.pfx");

    private static readonly TimeSpan RenewBefore = TimeSpan.FromDays(30);

    private readonly object sync = new();
    private readonly string pfxPath;
    private X509Certificate2? certificate;

    public CertificateProvider()
        : this(DefaultPfxPath)
    {
    }

    internal CertificateProvider(string pfxPath)
    {
        this.pfxPath = pfxPath;
    }

    public X509Certificate2 GetOrCreate()
    {
        lock (sync)
        {
            if (certificate != null)
            {
                return certificate;
            }

            certificate = TryLoad() ?? CreateAndPersist();
            return certificate;
        }
    }

    public string GetFingerprint() =>
        Convert.ToHexString(SHA256.HashData(GetOrCreate().RawData));

    private X509Certificate2? TryLoad()
    {
        try
        {
            if (!File.Exists(pfxPath))
            {
                return null;
            }

            var loaded = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, password: null);
            if (loaded.HasPrivateKey && loaded.NotAfter.ToUniversalTime() - DateTime.UtcNow > RenewBefore)
            {
                return loaded;
            }

            loaded.Dispose();
        }
        catch (Exception ex)
        {
            CaptureLog.Write($"[musicsync] certificate unreadable, regenerating: {ex.Message}");
        }

        return null;
    }

    private X509Certificate2 CreateAndPersist()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=ScreenShotManager Music Sync", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false)); // serverAuth

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddDnsName(Environment.MachineName);
        request.CertificateExtensions.Add(san.Build());

        var now = DateTimeOffset.UtcNow;
        using var created = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(10));
        var pfx = created.Export(X509ContentType.Pfx);

        FileHelper.EnsureDirectoryExists(pfxPath);
        File.WriteAllBytes(pfxPath, pfx);
        CaptureLog.Write("[musicsync] generated new TLS certificate; phones must re-pair");

        // Reload from PFX: SChannel can't use the ephemeral key CreateSelfSigned returns.
        return X509CertificateLoader.LoadPkcs12(pfx, password: null);
    }
}
