using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ScreenShotManager.MusicSync.Models;
using ScreenShotManager.MusicSync.Services;

namespace ScreenShotManager.Tests.MusicSync;

public class MusicLibraryServiceTests
{
    private static readonly byte[] SongA = Encoding.ASCII.GetBytes("fake mp3 bytes A");
    private static readonly byte[] SongB = Encoding.ASCII.GetBytes("fake flac bytes B");

    /// <summary>Long debounce so watcher-triggered rescans never race the explicit ones in a test.</summary>
    private static MusicLibraryService NewService(FakeMetadataReader reader) =>
        new(reader, new InMemoryTrackCache(), TimeSpan.FromMinutes(10));

    private static async Task<MusicLibraryService> StartAndScanAsync(FakeMetadataReader reader, string folder)
    {
        var service = NewService(reader);
        service.Start(folder);
        await service.RescanAsync();
        return service;
    }

    [Fact]
    public async Task Scan_includes_only_supported_audio_recursively()
    {
        using var temp = new TempFolder();
        temp.Write("a.mp3", SongA);
        temp.Write(@"Album\b.FLAC", SongB);
        temp.Write("cover.jpg", new byte[] { 1 });
        temp.Write("notes.txt", new byte[] { 1 });

        using var service = await StartAndScanAsync(new FakeMetadataReader(), temp.Path);

        Assert.True(service.IsReady);
        Assert.Equal(new[] { "Album/b.FLAC", "a.mp3" }, service.Current.Tracks.Select(t => t.RelativePath).OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Track_fields_come_from_file_and_metadata_with_filename_fallback()
    {
        using var temp = new TempFolder();
        var tagged = temp.Write("tagged.mp3", SongA);
        temp.Write("untagged.mp3", SongB);
        var reader = new FakeMetadataReader
        {
            Metadata = p => p == tagged
                ? new TrackMetadata("Song", "Artist", "Album", 3, 1234, true)
                : TrackMetadata.None,
        };

        using var service = await StartAndScanAsync(reader, temp.Path);

        var t = service.Current.Tracks.Single(x => x.RelativePath == "tagged.mp3");
        Assert.Equal("Song", t.Title);
        Assert.Equal("Artist", t.Artist);
        Assert.Equal("Album", t.Album);
        Assert.Equal(3u, t.TrackNumber);
        Assert.Equal(1234L, t.DurationMs);
        Assert.True(t.HasArt);
        Assert.Equal(SongA.Length, t.SizeBytes);
        Assert.Equal("audio/mpeg", t.ContentType);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(SongA)).ToLowerInvariant(), t.Sha256);
        Assert.Equal(tagged, t.FullPath);

        Assert.Equal("untagged", service.Current.Tracks.Single(x => x.RelativePath == "untagged.mp3").Title);
    }

    [Fact]
    public async Task Ids_are_stable_path_based_and_looked_up()
    {
        using var temp = new TempFolder();
        temp.Write(@"Sub\Song.mp3", SongA);

        using var service = await StartAndScanAsync(new FakeMetadataReader(), temp.Path);

        var track = service.Current.Tracks.Single();
        Assert.Equal(MusicLibraryService.ComputeTrackId("Sub/Song.mp3"), track.Id);
        Assert.Equal(MusicLibraryService.ComputeTrackId("sub/song.MP3"), track.Id);
        Assert.Matches("^[0-9a-f]{32}$", track.Id);
        Assert.True(service.TryGetTrack(track.Id, out var found));
        Assert.Same(track, found);
        Assert.False(service.TryGetTrack("nope", out _));
    }

    [Fact]
    public async Task Revision_changes_on_add_and_content_change_but_not_on_noop_rescan()
    {
        using var temp = new TempFolder();
        var path = temp.Write("a.mp3", SongA);
        using var service = await StartAndScanAsync(new FakeMetadataReader(), temp.Path);
        var first = service.Current.Revision;

        await service.RescanAsync();
        Assert.Equal(first, service.Current.Revision);

        temp.Write("b.mp3", SongB);
        await service.RescanAsync();
        var second = service.Current.Revision;
        Assert.NotEqual(first, second);

        File.WriteAllBytes(path, SongB);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        await service.RescanAsync();
        Assert.NotEqual(second, service.Current.Revision);
    }

    [Fact]
    public async Task Unchanged_files_are_served_from_cache()
    {
        using var temp = new TempFolder();
        temp.Write("a.mp3", SongA);
        temp.Write("b.mp3", SongB);
        var reader = new FakeMetadataReader();
        using var service = await StartAndScanAsync(reader, temp.Path);
        Assert.Equal(2, reader.ReadCount);

        await service.RescanAsync();

        Assert.Equal(2, reader.ReadCount);
    }

    [Fact]
    public async Task ManifestChanged_fires_only_when_revision_changes()
    {
        using var temp = new TempFolder();
        temp.Write("a.mp3", SongA);
        using var service = NewService(new FakeMetadataReader());
        var raised = 0;
        service.ManifestChanged += (_, _) => Interlocked.Increment(ref raised);

        service.Start(temp.Path);
        await service.RescanAsync();
        await service.RescanAsync();

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Missing_folder_yields_ready_empty_manifest()
    {
        var missing = Path.Combine(Path.GetTempPath(), "ssm-missing-" + Guid.NewGuid().ToString("N"));
        using var service = await StartAndScanAsync(new FakeMetadataReader(), missing);

        Assert.True(service.IsReady);
        Assert.Empty(service.Current.Tracks);
    }

    [Fact]
    public async Task Locked_file_is_skipped_until_released()
    {
        using var temp = new TempFolder();
        var path = temp.Write("copying.mp3", SongA);
        using var service = NewService(new FakeMetadataReader());
        service.Start(temp.Path);

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await service.RescanAsync();
            Assert.Empty(service.Current.Tracks);
        }

        await service.RescanAsync();
        Assert.Single(service.Current.Tracks);
    }

    [Fact]
    public async Task Stop_clears_ready_flag()
    {
        using var temp = new TempFolder();
        using var service = await StartAndScanAsync(new FakeMetadataReader(), temp.Path);

        service.Stop();

        Assert.False(service.IsReady);
    }
}
