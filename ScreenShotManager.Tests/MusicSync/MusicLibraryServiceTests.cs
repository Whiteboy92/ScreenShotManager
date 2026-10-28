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

    [Fact]
    public async Task Analysis_runs_in_background_and_lands_in_manifest_with_album_gain()
    {
        using var temp = new TempFolder();
        temp.Write("a.mp3", SongA);
        temp.Write("b.mp3", SongB);
        var reader = new FakeMetadataReader
        {
            Metadata = _ => new TrackMetadata("T", "Artist", "Album", 1, 60_000, false),
        };
        var analyzer = new FakeAnalyzer();
        using var service = new MusicLibraryService(reader, new InMemoryTrackCache(), TimeSpan.FromMilliseconds(50), analyzer);
        service.Start(temp.Path);
        await service.RescanAsync();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (service.Current.Tracks.Any(t => !t.HasWaveform) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.All(service.Current.Tracks, t =>
        {
            Assert.True(t.HasWaveform);
            Assert.Equal(-6.0, t.GainDb);
            Assert.Equal(-6.0, t.AlbumGainDb);
            Assert.NotNull(t.Waveform);
        });
        Assert.Equal(2, analyzer.Calls);
    }

    [Fact]
    public async Task Lrc_file_sets_lyrics_version_and_changes_revision()
    {
        using var temp = new TempFolder();
        temp.Write("Song.mp3", SongA);
        using var service = await StartAndScanAsync(new FakeMetadataReader(), temp.Path);
        Assert.Null(service.Current.Tracks.Single().LyricsVersion);
        var before = service.Current.Revision;

        temp.Write("Song.lrc", Encoding.UTF8.GetBytes("[00:01.00]Hello"));
        await service.RescanAsync();

        Assert.StartsWith("lrc-", service.Current.Tracks.Single().LyricsVersion);
        Assert.NotEqual(before, service.Current.Revision);
    }

    [Fact]
    public async Task Scan_does_not_follow_a_junction_out_of_the_music_folder()
    {
        using var music = new TempFolder();
        using var outside = new TempFolder();
        music.Write("inside.mp3", SongA);
        outside.Write("secret.mp3", SongB);
        CreateJunction(System.IO.Path.Combine(music.Path, "Linked"), outside.Path);

        using var service = await StartAndScanAsync(new FakeMetadataReader(), music.Path);

        Assert.Equal(new[] { "inside.mp3" }, service.Current.Tracks.Select(t => t.RelativePath));
    }

    [Fact]
    public async Task Scan_skips_a_file_symlink_pointing_out_of_the_music_folder()
    {
        using var music = new TempFolder();
        using var outside = new TempFolder();
        music.Write("inside.mp3", SongA);
        var secret = outside.Write("passwords.txt", SongB);
        try
        {
            File.CreateSymbolicLink(System.IO.Path.Combine(music.Path, "looks-like-a-song.mp3"), secret);
        }
        catch (IOException)
        {
            return; // Needs Developer Mode or admin; nothing to test without it.
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        using var service = await StartAndScanAsync(new FakeMetadataReader(), music.Path);

        Assert.Equal(new[] { "inside.mp3" }, service.Current.Tracks.Select(t => t.RelativePath));
    }

    [Fact]
    public async Task Track_folder_swapped_for_a_junction_after_the_scan_is_not_served()
    {
        using var music = new TempFolder();
        using var outside = new TempFolder();
        music.Write(@"Album\song.mp3", SongA);
        outside.Write("song.mp3", SongB);
        using var service = await StartAndScanAsync(new FakeMetadataReader(), music.Path);
        var id = service.Current.Tracks.Single().Id;

        Directory.Delete(System.IO.Path.Combine(music.Path, "Album"), recursive: true);
        CreateJunction(System.IO.Path.Combine(music.Path, "Album"), outside.Path);

        Assert.False(service.TryGetTrack(id, out _));
    }

    [Theory]
    [InlineData(@"..\outside\song.mp3")]
    [InlineData(@"C:\Windows\win.ini")]
    public void Paths_outside_the_music_folder_are_rejected(string relative)
    {
        using var music = new TempFolder();
        Assert.False(LibraryPaths.IsPlainFileInside(music.Path, System.IO.Path.Combine(music.Path, relative)));
    }

    /// <summary>Junctions need no admin rights, unlike directory symlinks.</summary>
    private static void CreateJunction(string link, string target)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            ArgumentList = { "/c", "mklink", "/J", link, target },
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
        process.WaitForExit();
        Assert.True(Directory.Exists(link), "could not create junction");
    }
}
