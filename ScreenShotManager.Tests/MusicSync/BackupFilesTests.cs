using System;
using System.IO;
using System.Linq;
using ScreenShotManager.MusicSync.Services;

namespace ScreenShotManager.Tests.MusicSync;

public sealed class BackupFilesTests : IDisposable
{
    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    [Fact]
    public void Sources_list_other_devices_and_the_shared_backup_but_not_own_or_previous_copies()
    {
        var folder = temp.Path;
        BackupFiles.Write(folder, "aaaaaaaaaaaa", "{}"u8.ToArray());
        BackupFiles.Write(folder, "aaaaaaaaaaaa", "{}"u8.ToArray()); // creates a .previous copy
        BackupFiles.Write(folder, "bbbbbbbbbbbb", "{}"u8.ToArray());
        File.WriteAllText(BackupFiles.LegacyPath(folder), "{}");
        File.WriteAllText(Path.Combine(folder, "musicwave-backup.previous.json"), "{}");
        File.WriteAllText(Path.Combine(folder, "musicwave-backup.NOT-AN-ID.json"), "{}");

        var sources = BackupFiles.Sources(folder, "aaaaaaaaaaaa");

        Assert.Equal(new string?[] { null, "bbbbbbbbbbbb" }, sources.Select(s => s.DeviceId).OrderBy(id => id));
    }

    [Fact]
    public void Copying_a_backup_keeps_the_targets_old_one_as_previous()
    {
        var folder = temp.Path;
        BackupFiles.Write(folder, "aaaaaaaaaaaa", """{"a":1}"""u8.ToArray());
        BackupFiles.Write(folder, "bbbbbbbbbbbb", """{"b":1}"""u8.ToArray());

        BackupFiles.CopyTo(folder, BackupFiles.Sources(folder, "aaaaaaaaaaaa").Single(), "aaaaaaaaaaaa");

        Assert.Equal("""{"b":1}""", File.ReadAllText(BackupFiles.PathFor(folder, "aaaaaaaaaaaa")));
        Assert.Equal("""{"a":1}""", File.ReadAllText(BackupFiles.PreviousPathFor(folder, "aaaaaaaaaaaa")));
    }

    [Theory]
    [InlineData(@"..\x")]
    [InlineData("ABC")]
    [InlineData("")]
    public void Bad_device_ids_never_become_paths(string id) =>
        Assert.Throws<ArgumentException>(() => BackupFiles.PathFor(temp.Path, id));
}
