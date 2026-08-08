using System;
using System.IO;
using System.Threading;
using ScreenShotManager.Screenshot.Models;
using ScreenShotManager.Screenshot.Services;

namespace ScreenShotManager.Tests.Screenshot;

public class SettingsServiceTests
{
    [Fact]
    public void CleanupOldFiles_keeps_only_newest_within_limit()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ssm-clean-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            for (var i = 0; i < 5; i++)
            {
                var path = Path.Combine(dir, $"shot_{i}.png");
                File.WriteAllText(path, "x");
                File.SetCreationTime(path, DateTime.Now.AddSeconds(i));
                Thread.Sleep(5);
            }

            new SettingsService().CleanupOldFiles(new AppSettings
            {
                SaveFolder = dir,
                MaxAutoSaveHistory = 3,
            });

            var remaining = Directory.GetFiles(dir);
            Assert.Equal(3, remaining.Length);
            Assert.Contains(remaining, p => p.EndsWith("shot_4.png"));
            Assert.Contains(remaining, p => p.EndsWith("shot_3.png"));
            Assert.Contains(remaining, p => p.EndsWith("shot_2.png"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CleanupOldFiles_noop_when_limit_zero_or_negative()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ssm-clean-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            for (var i = 0; i < 3; i++)
            {
                File.WriteAllText(Path.Combine(dir, $"s{i}.png"), "x");
            }

            new SettingsService().CleanupOldFiles(new AppSettings { SaveFolder = dir, MaxAutoSaveHistory = 0 });

            Assert.Equal(3, Directory.GetFiles(dir).Length);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CleanupOldFiles_ignores_non_image_files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ssm-clean-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.png"), "x");
            File.WriteAllText(Path.Combine(dir, "b.png"), "x");
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");

            new SettingsService().CleanupOldFiles(new AppSettings { SaveFolder = dir, MaxAutoSaveHistory = 1 });

            Assert.True(File.Exists(Path.Combine(dir, "notes.txt")));
            Assert.Single(Directory.GetFiles(dir, "*.png"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CleanupOldFiles_silent_when_folder_missing()
    {
        new SettingsService().CleanupOldFiles(new AppSettings
        {
            SaveFolder = Path.Combine(Path.GetTempPath(), "ssm-missing-" + Guid.NewGuid().ToString("N")),
            MaxAutoSaveHistory = 5,
        });
    }
}
