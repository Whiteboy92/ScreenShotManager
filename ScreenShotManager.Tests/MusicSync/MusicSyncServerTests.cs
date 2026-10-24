using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ScreenShotManager.MusicSync.Models;
using ScreenShotManager.MusicSync.Services;

namespace ScreenShotManager.Tests.MusicSync;

/// <summary>
/// End-to-end over real HTTPS on loopback: auth, manifest caching, ranged downloads, art and
/// certificate pinning — the contract the Android app relies on.
/// </summary>
public sealed class MusicSyncServerTests : IAsyncLifetime
{
    private const string Token = "test-token";
    private static readonly byte[] Song = Encoding.ASCII.GetBytes("0123456789abcdefghij");

    private readonly TempFolder temp = new();
    private readonly FakeMetadataReader reader = new();
    private MusicLibraryService library = null!;
    private CertificateProvider certificates = null!;
    private MusicSyncServer server = null!;
    private DeviceRegistry devices = null!;
    private HttpClient client = null!;
    private string trackId = string.Empty;
    private readonly ManualClock clock = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> log = new();

    public async Task InitializeAsync()
    {
        var musicFolder = Path.Combine(temp.Path, "music");
        temp.Write(@"music\song.mp3", Song);

        library = new MusicLibraryService(reader, new InMemoryTrackCache(), TimeSpan.FromMinutes(10));
        library.Start(musicFolder);
        await library.RescanAsync();
        trackId = library.Current.Tracks.Single().Id;

        certificates = new CertificateProvider(Path.Combine(temp.Path, "cert.pfx"));
        devices = new DeviceRegistry(Path.Combine(temp.Path, "devices.json"), clock);
        devices.ImportLegacyToken(Token, "Test phone");
        server = new MusicSyncServer(library, reader, devices) { LoopbackOnly = true, Clock = clock, Log = log.Enqueue };
        await server.StartAsync(0, certificates.GetOrCreate());

        client = NewPinnedClient(certificates.GetFingerprint());
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await server.DisposeAsync();
        library.Dispose();
        temp.Dispose();
    }

    /// <summary>Mirrors what the phone does: trust exactly the certificate fingerprint from the QR code.</summary>
    private HttpClient NewPinnedClient(string fingerprint)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                cert != null && Convert.ToHexString(SHA256.HashData(cert.RawData)) == fingerprint,
        };

        return new HttpClient(handler) { BaseAddress = new Uri($"https://localhost:{server.BoundPort}") };
    }

    private HttpRequestMessage Get(string path, string? token = Token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    [Fact]
    public void Binds_an_ephemeral_port()
    {
        Assert.True(server.IsRunning);
        Assert.InRange(server.BoundPort, 1, 65535);
    }

    [Fact]
    public async Task Ping_needs_no_token()
    {
        var response = await client.SendAsync(Get("/api/ping", token: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(MusicSyncConstants.ApiVersion, json.RootElement.GetProperty("apiVersion").GetInt32());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    public async Task Api_routes_reject_missing_or_wrong_token(string? token)
    {
        foreach (var path in new[] { "/api/manifest", $"/api/tracks/{trackId}/file", $"/api/tracks/{trackId}/art" })
        {
            var response = await client.SendAsync(Get(path, token));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Manifest_is_camelCase_json_hides_local_path_and_supports_etag()
    {
        var response = await client.SendAsync(Get("/api/manifest"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var track = json.RootElement.GetProperty("tracks").EnumerateArray().Single();
        Assert.Equal(trackId, track.GetProperty("id").GetString());
        Assert.Equal("song.mp3", track.GetProperty("relativePath").GetString());
        Assert.Equal(Song.Length, track.GetProperty("sizeBytes").GetInt64());
        Assert.False(track.TryGetProperty("fullPath", out _));
        Assert.DoesNotContain(temp.Path.Replace("\\", "\\\\"), body);

        var etag = response.Headers.ETag;
        Assert.NotNull(etag);

        var again = Get("/api/manifest");
        again.Headers.IfNoneMatch.Add(etag!);
        var notModified = await client.SendAsync(again);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
    }

    [Fact]
    public async Task Manifest_is_503_until_library_ready()
    {
        library.Stop();

        var response = await client.SendAsync(Get("/api/manifest"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
    }

    [Fact]
    public async Task File_download_returns_full_bytes()
    {
        var response = await client.SendAsync(Get($"/api/tracks/{trackId}/file"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("audio/mpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Song, await response.Content.ReadAsByteArrayAsync());
        Assert.Contains("bytes", response.Headers.AcceptRanges);
    }

    [Fact]
    public async Task File_download_resumes_with_range()
    {
        var request = Get($"/api/tracks/{trackId}/file");
        request.Headers.Range = new RangeHeaderValue(10, null);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(Song.Skip(10).ToArray(), await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(10, response.Content.Headers.ContentRange?.From);
        Assert.Equal(Song.Length, response.Content.Headers.ContentRange?.Length);
    }

    [Fact]
    public async Task Unknown_track_is_404_and_path_like_ids_are_not_resolved()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Get("/api/tracks/deadbeef/file"))).StatusCode);
        var traversal = await client.SendAsync(Get("/api/tracks/..%2F..%2Fcert.pfx/file"));
        Assert.Equal(HttpStatusCode.NotFound, traversal.StatusCode);
    }

    [Fact]
    public async Task Art_returns_image_or_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Get($"/api/tracks/{trackId}/art"))).StatusCode);

        reader.Art = new AlbumArt(new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg");
        var response = await client.SendAsync(Get($"/api/tracks/{trackId}/art"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF }, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Repeated_wrong_tokens_lock_the_address_out_then_expire()
    {
        for (var i = 0; i < MusicSyncConstants.MaxAuthFailures; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Get("/api/manifest", "wrong"))).StatusCode);
        }

        // Locked: even the right token is refused until the lockout ends, and the PC log says why.
        var locked = await client.SendAsync(Get("/api/manifest"));
        Assert.Equal((HttpStatusCode)429, locked.StatusCode);
        Assert.NotNull(locked.Headers.RetryAfter);
        Assert.Contains(log, line => line.Contains("locked out", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Get("/api/ping", token: null))).StatusCode);

        clock.Advance(MusicSyncConstants.AuthLockout + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Get("/api/manifest"))).StatusCode);
    }

    [Fact]
    public async Task Correct_token_resets_the_failure_count()
    {
        for (var round = 0; round < 3; round++)
        {
            for (var i = 0; i < MusicSyncConstants.MaxAuthFailures - 1; i++)
            {
                await client.SendAsync(Get("/api/manifest", "wrong"));
            }

            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Get("/api/manifest"))).StatusCode);
        }
    }

    [Fact]
    public async Task Removing_a_device_revokes_its_token()
    {
        devices.Remove(devices.Devices.Single().Id);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Get("/api/manifest"))).StatusCode);
    }

    private Task<HttpResponseMessage> Pair(string code, string name = "Pixel") =>
        client.PostAsync("/api/pair", new StringContent(
            JsonSerializer.Serialize(new { code, deviceName = name }), Encoding.UTF8, "application/json"));

    [Fact]
    public async Task Pairing_code_buys_a_device_token_once()
    {
        var code = devices.IssuePairingCode().Code;

        var response = await Pair(code, "Pixel\u0007 9");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("token").GetString()!;

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Get("/api/manifest", token))).StatusCode);
        Assert.Contains(devices.Devices, d => d.Name == "Pixel 9");
        Assert.DoesNotContain(token, File.ReadAllText(Path.Combine(temp.Path, "devices.json")), StringComparison.OrdinalIgnoreCase);

        // Spent: a second phone (or a replay) can't reuse it.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Pair(code)).StatusCode);
    }

    [Fact]
    public async Task Expired_or_wrong_pairing_codes_are_refused()
    {
        var code = devices.IssuePairingCode().Code;
        Assert.Equal(HttpStatusCode.Unauthorized, (await Pair("nope")).StatusCode);

        clock.Advance(MusicSyncConstants.PairingCodeLifetime);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Pair(code)).StatusCode);
        Assert.Single(devices.Devices);
    }

    [Fact]
    public async Task Wrong_pairing_codes_count_toward_the_lockout()
    {
        for (var i = 0; i < MusicSyncConstants.MaxAuthFailures; i++)
        {
            await Pair("guess" + i);
        }

        var code = devices.IssuePairingCode().Code;
        Assert.Equal((HttpStatusCode)429, (await Pair(code)).StatusCode);
    }

    [Fact]
    public async Task Manifest_is_cached_per_revision()
    {
        var first = await (await client.SendAsync(Get("/api/manifest"))).Content.ReadAsStringAsync();
        var second = await (await client.SendAsync(Get("/api/manifest"))).Content.ReadAsStringAsync();
        Assert.Equal(first, second);

        temp.Write(@"music\other.mp3", Song);
        await library.RescanAsync();
        var third = await (await client.SendAsync(Get("/api/manifest"))).Content.ReadAsStringAsync();
        Assert.NotEqual(first, third);
    }

    [Fact]
    public async Task Client_pinning_a_different_fingerprint_refuses_to_connect()
    {
        using var impostor = NewPinnedClient(new string('0', 64));

        await Assert.ThrowsAsync<HttpRequestException>(() => impostor.SendAsync(Get("/api/ping", token: null)));
    }

    private HttpRequestMessage PutBackup(string body, string? token = Token)
    {
        var request = Get("/api/backup", token);
        request.Method = HttpMethod.Put;
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return request;
    }

    [Fact]
    public async Task Backup_round_trips_and_keeps_previous_copy()
    {
        var folder = Path.Combine(temp.Path, "Music Settings");
        server.BackupFolder = folder;

        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Get("/api/backup"))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(PutBackup("""{"v":1}"""))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(PutBackup("""{"v":2}"""))).StatusCode);

        var response = await client.SendAsync(Get("/api/backup"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"v":2}""", await response.Content.ReadAsStringAsync());
        Assert.Equal("""{"v":1}""", File.ReadAllText(Path.Combine(folder, MusicSyncConstants.PreviousBackupFileName)));
    }

    [Fact]
    public async Task Backup_rejects_bad_json_missing_token_and_disabled_folder()
    {
        var folder = Path.Combine(temp.Path, "Music Settings");
        server.BackupFolder = folder;

        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(PutBackup("{ not json"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(PutBackup("{}", token: null))).StatusCode);
        Assert.False(Directory.Exists(folder));

        server.BackupFolder = null;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.SendAsync(PutBackup("{}"))).StatusCode);
    }

    [Fact]
    public async Task Backup_over_size_limit_is_rejected()
    {
        server.BackupFolder = Path.Combine(temp.Path, "Music Settings");
        var huge = "\"" + new string('x', MusicSyncConstants.MaxBackupBytes + 10) + "\"";

        // Kestrel may answer 413 or reset the connection mid-upload; either way nothing is stored.
        try
        {
            var response = await client.SendAsync(PutBackup(huge));
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        }
        catch (HttpRequestException)
        {
        }

        Assert.False(File.Exists(Path.Combine(server.BackupFolder!, MusicSyncConstants.BackupFileName)));
    }

    [Fact]
    public void Certificate_is_persisted_and_reused()
    {
        var reloaded = new CertificateProvider(Path.Combine(temp.Path, "cert.pfx"));

        Assert.Equal(certificates.GetFingerprint(), reloaded.GetFingerprint());
        Assert.True(reloaded.GetOrCreate().HasPrivateKey);
    }
}
