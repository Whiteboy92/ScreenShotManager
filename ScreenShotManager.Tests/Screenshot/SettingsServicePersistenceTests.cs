using System;
using System.IO;
using ScreenShotManager.Screenshot.Models;
using ScreenShotManager.Screenshot.Services;

namespace ScreenShotManager.Tests.Screenshot;

public class SettingsServicePersistenceTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "ssm-cfg-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    private string ConfigPath => Path.Combine(dir, "nested", "config.json");

    [Fact]
    public void Save_then_Load_round_trips_all_fields()
    {
        var svc = new SettingsService(ConfigPath);
        var original = new AppSettings
        {
            SaveFolder = @"D:\Shots",
            FileFormat = "JPEG",
            FilenamePattern = "cap_{date}",
            DisableDimming = true,
            StartOnLogin = true,
            MaxAutoSaveHistory = 7,
        };

        svc.Save(original);
        var loaded = svc.Load();

        Assert.Equal(original.SaveFolder, loaded.SaveFolder);
        Assert.Equal(original.FileFormat, loaded.FileFormat);
        Assert.Equal(original.FilenamePattern, loaded.FilenamePattern);
        Assert.Equal(original.DisableDimming, loaded.DisableDimming);
        Assert.Equal(original.StartOnLogin, loaded.StartOnLogin);
        Assert.Equal(original.MaxAutoSaveHistory, loaded.MaxAutoSaveHistory);
    }

    [Fact]
    public void Save_creates_missing_directory()
    {
        new SettingsService(ConfigPath).Save(new AppSettings());
        Assert.True(File.Exists(ConfigPath));
    }

    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        var loaded = new SettingsService(ConfigPath).Load();
        Assert.Equal(new AppSettings().MaxAutoSaveHistory, loaded.MaxAutoSaveHistory);
        Assert.Equal("PNG", loaded.FileFormat);
    }

    [Theory]
    [InlineData("{ not valid json")]
    [InlineData("null")]
    [InlineData("")]
    public void Load_returns_defaults_on_bad_content(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, content);

        var loaded = new SettingsService(ConfigPath).Load();

        Assert.NotNull(loaded);
        Assert.Equal("PNG", loaded.FileFormat);
    }

    [Fact]
    public void Load_fills_defaults_for_missing_properties()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, """{"FileFormat":"BMP"}""");

        var loaded = new SettingsService(ConfigPath).Load();

        Assert.Equal("BMP", loaded.FileFormat);
        Assert.Equal(100, loaded.MaxAutoSaveHistory);
        Assert.Equal("Screenshot_{date}_{time}", loaded.FilenamePattern);
    }

    [Fact]
    public void Save_wraps_io_failure()
    {
        // Config path occupied by a directory -> WriteAllText fails.
        Directory.CreateDirectory(ConfigPath);

        var ex = Assert.Throws<InvalidOperationException>(() => new SettingsService(ConfigPath).Save(new AppSettings()));
        Assert.StartsWith("Failed to save settings", ex.Message);
        Assert.NotNull(ex.InnerException);
    }
}
