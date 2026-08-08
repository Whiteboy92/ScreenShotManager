using System;
using System.Text.RegularExpressions;
using ScreenShotManager.Screenshot.Models;

namespace ScreenShotManager.Tests.Screenshot;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_are_sane()
    {
        var s = new AppSettings();
        Assert.Equal("PNG", s.FileFormat);
        Assert.Equal("Screenshot_{date}_{time}", s.FilenamePattern);
        Assert.Equal(100, s.MaxAutoSaveHistory);
        Assert.False(s.DisableDimming);
        Assert.False(s.StartOnLogin);
        Assert.EndsWith("Screenshots", s.SaveFolder);
    }

    [Fact]
    public void GetFileName_substitutes_date_time_and_extension()
    {
        var s = new AppSettings { FilenamePattern = "shot_{date}_{time}", FileFormat = "PNG" };
        var name = s.GetFileName();
        Assert.Matches(new Regex(@"^shot_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}\.png$"), name);
    }

    [Fact]
    public void GetFileName_lowercases_extension()
    {
        var s = new AppSettings { FilenamePattern = "x", FileFormat = "JPEG" };
        Assert.EndsWith(".jpeg", s.GetFileName());
    }

    [Fact]
    public void GetFileName_leaves_unknown_tokens_alone()
    {
        var s = new AppSettings { FilenamePattern = "img_{foo}", FileFormat = "PNG" };
        Assert.Equal("img_{foo}.png", s.GetFileName());
    }
}
