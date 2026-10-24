using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using ScreenShotManager.MusicSync.Models;
using ScreenShotManager.MusicSync.Services.Interfaces;

namespace ScreenShotManager.MusicSync.Services;

/// <summary>
/// Kestrel-hosted LAN API. Routes:
/// <list type="bullet">
/// <item><c>GET /api/ping</c> — unauthenticated liveness + API version.</item>
/// <item><c>GET /api/manifest</c> — full track list; ETag/If-None-Match, 503 until the first scan finishes.</item>
/// <item><c>GET /api/tracks/{id}/file</c> — audio bytes with Range support for resumable downloads.</item>
/// <item><c>GET /api/tracks/{id}/art</c> — cover image, 404 when none.</item>
/// </list>
/// Tracks are only reachable by manifest id, never by a client-supplied path.
/// </summary>
public sealed class MusicSyncServer : IMusicSyncServer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IMusicLibraryService library;
    private readonly ITrackMetadataReader metadataReader;
    private WebApplication? app;
    private volatile string token = string.Empty;
    private volatile string? backupFolder;

    public MusicSyncServer(IMusicLibraryService library, ITrackMetadataReader metadataReader)
    {
        this.library = library;
        this.metadataReader = metadataReader;
    }

    public bool IsRunning => app != null;

    public int BoundPort { get; private set; }

    public string? BackupFolder
    {
        get => backupFolder;
        set => backupFolder = value;
    }

    /// <summary>Tests bind to loopback so Windows Firewall doesn't prompt for testhost.exe.</summary>
    internal bool LoopbackOnly { get; init; }

    public async Task StartAsync(int port, string accessToken, X509Certificate2 certificate, CancellationToken cancellationToken = default)
    {
        if (app != null)
        {
            throw new InvalidOperationException("Music sync server is already running.");
        }

        token = accessToken;

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrelHttpsConfiguration();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            // Only PUT /api/backup has a body.
            kestrel.Limits.MaxRequestBodySize = MusicSyncConstants.MaxBackupBytes;
            if (LoopbackOnly)
            {
                // ListenLocalhost can't bind port 0, so use the IPv4 loopback explicitly.
                kestrel.Listen(IPAddress.Loopback, port, listen => listen.UseHttps(certificate));
            }
            else
            {
                kestrel.ListenAnyIP(port, listen => listen.UseHttps(certificate));
            }
        });

        var web = builder.Build();
        MapRoutes(web);

        try
        {
            await web.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await web.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        app = web;
        BoundPort = ResolveBoundPort(web, port);
    }

    public async Task StopAsync()
    {
        var running = app;
        app = null;
        BoundPort = 0;

        if (running == null)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await running.StopAsync(timeout.Token).ConfigureAwait(false);
        }
        finally
        {
            await running.DisposeAsync().ConfigureAwait(false);
        }
    }

    public void UpdateToken(string accessToken) => token = accessToken;

    private void MapRoutes(WebApplication web)
    {
        web.MapGet("/api/ping", () => Results.Json(
            new { app = "ScreenShotManager", apiVersion = MusicSyncConstants.ApiVersion, name = Environment.MachineName },
            JsonOptions));

        var api = web.MapGroup("/api").AddEndpointFilter(async (context, next) =>
            PairingTokens.IsAuthorized(context.HttpContext.Request.Headers.Authorization.ToString(), token)
                ? await next(context)
                : Results.Unauthorized());

        api.MapGet("/manifest", GetManifest);
        api.MapGet("/tracks/{id}/file", GetTrackFile);
        api.MapGet("/tracks/{id}/art", GetTrackArt);
        // Cast: a (HttpContext) -> Task method group would bind as a raw RequestDelegate, which
        // skips endpoint filters (the token check!) and discards the IResult.
        api.MapPut("/backup", (Delegate)PutBackupAsync);
        api.MapGet("/tracks/{id}/waveform", GetWaveform);
        api.MapGet("/tracks/{id}/lyrics", GetLyrics);
        api.MapGet("/backup", GetBackup);
    }

    /// <summary>
    /// Stores the phone's settings/playlists JSON. Must parse as JSON; written atomically, and the
    /// previous backup is kept alongside so a bad upload never leaves you with nothing.
    /// </summary>
    private async Task<IResult> PutBackupAsync(HttpContext http)
    {
        var folder = backupFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        byte[] body;
        try
        {
            using var buffer = new MemoryStream();
            await http.Request.Body.CopyToAsync(buffer, http.RequestAborted).ConfigureAwait(false);
            body = buffer.ToArray();
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        try
        {
            using var _ = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "Backup must be valid JSON" });
        }

        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, MusicSyncConstants.BackupFileName);
        var temp = target + ".tmp";
        await File.WriteAllBytesAsync(temp, body, http.RequestAborted).ConfigureAwait(false);

        if (File.Exists(target))
        {
            File.Copy(target, Path.Combine(folder, MusicSyncConstants.PreviousBackupFileName), overwrite: true);
        }

        File.Move(temp, target, overwrite: true);
        return Results.NoContent();
    }

    private IResult GetBackup()
    {
        var folder = backupFolder;
        var path = string.IsNullOrWhiteSpace(folder) ? null : Path.Combine(folder, MusicSyncConstants.BackupFileName);
        return path != null && File.Exists(path)
            ? Results.File(path, "application/json")
            : Results.NotFound();
    }

    private IResult GetManifest(HttpContext http)
    {
        if (!library.IsReady)
        {
            http.Response.Headers.RetryAfter = "5";
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var manifest = library.Current;
        var etag = $"\"{manifest.Revision}\"";

        if (http.Request.Headers.IfNoneMatch.Contains(etag))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        http.Response.Headers.ETag = etag;
        http.Response.Headers.CacheControl = "no-cache";
        return Results.Json(manifest, JsonOptions);
    }

    private IResult GetTrackFile(string id)
    {
        if (!library.TryGetTrack(id, out var track) || !File.Exists(track.FullPath))
        {
            return Results.NotFound();
        }

        return Results.File(
            track.FullPath,
            track.ContentType,
            fileDownloadName: Path.GetFileName(track.FullPath),
            lastModified: track.ModifiedUtc,
            entityTag: new EntityTagHeaderValue($"\"{track.Sha256}\""),
            enableRangeProcessing: true);
    }

    private IResult GetTrackArt(string id, HttpContext http)
    {
        if (!library.TryGetTrack(id, out var track))
        {
            return Results.NotFound();
        }

        var art = metadataReader.ReadArt(track.FullPath);
        if (art == null)
        {
            return Results.NotFound();
        }

        http.Response.Headers.CacheControl = "private, max-age=86400";
        return Results.Bytes(art.Data, art.ContentType);
    }

    /// <summary>One byte (0–255) per waveform point.</summary>
    private IResult GetWaveform(string id) =>
        library.TryGetTrack(id, out var track) && track.Waveform is { Length: > 0 } waveform
            ? Results.Bytes(waveform, "application/octet-stream")
            : Results.NotFound();

    /// <summary>LRC (synced) or plain lyrics as UTF-8 text.</summary>
    private IResult GetLyrics(string id)
    {
        if (!library.TryGetTrack(id, out var track) || track.LyricsVersion == null)
        {
            return Results.NotFound();
        }

        var lyrics = metadataReader.ReadLyrics(track.FullPath);
        return lyrics == null ? Results.NotFound() : Results.Text(lyrics, "text/plain; charset=utf-8");
    }

    private static int ResolveBoundPort(WebApplication web, int requestedPort)
    {
        var address = web.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();

        if (address != null
            && Uri.TryCreate(address.Replace("[::]", "localhost").Replace("0.0.0.0", "localhost"), UriKind.Absolute, out var uri))
        {
            return uri.Port;
        }

        return requestedPort;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
