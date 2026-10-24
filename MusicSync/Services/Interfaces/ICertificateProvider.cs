using System.Security.Cryptography.X509Certificates;

namespace ScreenShotManager.MusicSync.Services.Interfaces;

/// <summary>
/// Owns the self-signed TLS certificate for the LAN API. Created once and persisted, so the
/// fingerprint the phone pinned at pairing stays valid across restarts.
/// </summary>
public interface ICertificateProvider
{
    X509Certificate2 GetOrCreate();

    /// <summary>Upper-case hex SHA-256 of the certificate's DER bytes (what the phone pins).</summary>
    string GetFingerprint();
}
