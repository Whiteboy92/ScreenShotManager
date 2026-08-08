using System;
using System.IO;
using ScreenShotManager.Shared.Helpers;

namespace ScreenShotManager.Tests.Shared;

public class FileHelperTests
{
    [Fact]
    public void EnsureDirectoryExists_creates_missing_directory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ssm-test-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(tempRoot, "a", "b", "file.png");
        try
        {
            FileHelper.EnsureDirectoryExists(nested);
            Assert.True(Directory.Exists(Path.GetDirectoryName(nested)!));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void EnsureDirectoryExists_noop_when_directory_present()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ssm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            FileHelper.EnsureDirectoryExists(Path.Combine(tempRoot, "file.png"));
            Assert.True(Directory.Exists(tempRoot));
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Theory]
    [InlineData(".png", true)]
    [InlineData(".PNG", true)]
    [InlineData(".jpg", true)]
    [InlineData(".jpeg", true)]
    [InlineData(".bmp", true)]
    [InlineData(".gif", false)]
    [InlineData(".txt", false)]
    [InlineData("", false)]
    public void IsImageFile_matches_supported_extensions(string ext, bool expected)
    {
        Assert.Equal(expected, FileHelper.IsImageFile(ext));
    }

    [Fact]
    public void TryDeleteFile_removes_existing_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "ssm-test-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(path, "x");

        FileHelper.TryDeleteFile(path);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void TryDeleteFile_swallows_missing_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "ssm-nope-" + Guid.NewGuid().ToString("N") + ".tmp");
        FileHelper.TryDeleteFile(path);
        Assert.False(File.Exists(path));
    }
}
