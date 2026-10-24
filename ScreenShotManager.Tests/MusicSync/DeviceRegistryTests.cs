using System;
using System.IO;
using System.Linq;
using ScreenShotManager.MusicSync.Models;
using ScreenShotManager.MusicSync.Services;

namespace ScreenShotManager.Tests.MusicSync;

public sealed class DeviceRegistryTests : IDisposable
{
    private readonly TempFolder temp = new();
    private readonly ManualClock clock = new();

    private string FilePath => Path.Combine(temp.Path, "devices.json");

    private DeviceRegistry NewRegistry() => new(FilePath, clock);

    public void Dispose() => temp.Dispose();

    [Fact]
    public void Each_redeemed_code_gets_its_own_token_and_survives_restart()
    {
        var registry = NewRegistry();
        var first = registry.Redeem(registry.IssuePairingCode().Code, "Phone A");
        var second = registry.Redeem(registry.IssuePairingCode().Code, "Phone B");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);

        var reloaded = NewRegistry();
        Assert.Equal("Phone A", reloaded.Authenticate(first)?.Name);
        Assert.Equal("Phone B", reloaded.Authenticate(second)?.Name);
        Assert.Null(reloaded.Authenticate("not-a-token"));
        Assert.Null(reloaded.Authenticate(null));
    }

    [Fact]
    public void Removing_one_device_keeps_the_others()
    {
        var registry = NewRegistry();
        var a = registry.Redeem(registry.IssuePairingCode().Code, "A")!;
        var b = registry.Redeem(registry.IssuePairingCode().Code, "B")!;

        Assert.True(registry.Remove(registry.Authenticate(a)!.Id));

        Assert.Null(registry.Authenticate(a));
        Assert.NotNull(registry.Authenticate(b));
    }

    [Fact]
    public void Issuing_a_new_code_invalidates_the_previous_one()
    {
        var registry = NewRegistry();
        var old = registry.IssuePairingCode().Code;
        registry.IssuePairingCode();

        Assert.Null(registry.Redeem(old, "x"));
    }

    [Fact]
    public void Legacy_token_is_imported_once_and_only_its_hash_is_stored()
    {
        var registry = NewRegistry();
        registry.ImportLegacyToken("legacy-secret", "Old phone");
        registry.ImportLegacyToken("legacy-secret", "Old phone");

        Assert.Single(registry.Devices);
        Assert.NotNull(registry.Authenticate("legacy-secret"));
        Assert.DoesNotContain("legacy-secret", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Last_seen_is_recorded_without_writing_on_every_request()
    {
        var registry = NewRegistry();
        registry.ImportLegacyToken("t", "Phone");

        var seen = registry.Authenticate("t")!.LastSeenUtc;
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(seen, registry.Authenticate("t")!.LastSeenUtc);

        clock.Advance(TimeSpan.FromMinutes(15));
        Assert.True(registry.Authenticate("t")!.LastSeenUtc > seen);
    }

    [Theory]
    [InlineData("  Pixel 9  ", "Pixel 9")]
    [InlineData("", "Phone")]
    [InlineData(null, "Phone")]
    [InlineData("a\r\nb", "ab")]
    public void Device_names_are_cleaned(string? input, string expected) =>
        Assert.Equal(expected, DeviceRegistry.CleanName(input));

    [Fact]
    public void Device_names_are_capped() =>
        Assert.Equal(40, DeviceRegistry.CleanName(new string('x', 100)).Length);
}

public sealed class CertificateProtectionTests : IDisposable
{
    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    [Fact]
    public void Certificate_file_is_dpapi_encrypted_and_reloads_with_same_fingerprint()
    {
        var path = Path.Combine(temp.Path, "cert.pfx");
        var fingerprint = new CertificateProvider(path).GetFingerprint();

        var bytes = File.ReadAllBytes(path);
        Assert.NotEqual(0x30, bytes[0]); // not a raw PKCS#12 DER file
        Assert.Throws<System.Security.Cryptography.CryptographicException>(
            () => System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(bytes, null));

        Assert.Equal(fingerprint, new CertificateProvider(path).GetFingerprint());
    }

    [Fact]
    public void Legacy_plain_pfx_is_kept_and_rewritten_encrypted()
    {
        var path = Path.Combine(temp.Path, "cert.pfx");
        // What older builds wrote: a raw, unencrypted PFX.
        using var key = System.Security.Cryptography.ECDsa.Create();
        using var original = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                "CN=legacy", key, System.Security.Cryptography.HashAlgorithmName.SHA256)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        File.WriteAllBytes(path, original.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx));
        Assert.Equal(0x30, File.ReadAllBytes(path)[0]);

        var fingerprint = new CertificateProvider(path).GetFingerprint();

        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original.RawData)), fingerprint);
        Assert.NotEqual(0x30, File.ReadAllBytes(path)[0]);
    }
}

public class FfmpegArgumentTests
{
    [Fact]
    public void Song_path_is_a_single_argument_even_with_quotes_and_spaces()
    {
        const string nasty = "C:\\Music\\a \" -y -f null x.mp3";
        var info = TrackAnalyzer.BuildStartInfo("ffmpeg.exe", nasty);

        var args = info.ArgumentList.ToList();
        Assert.Equal(nasty, args[args.IndexOf("-i") + 1]);
        Assert.DoesNotContain("-y", args);
        Assert.Equal(string.Empty, info.Arguments);
    }
}
