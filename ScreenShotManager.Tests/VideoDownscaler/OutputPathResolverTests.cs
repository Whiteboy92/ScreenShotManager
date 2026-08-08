using System;
using System.IO;
using ScreenShotManager.VideoDownscaler.Services;

namespace ScreenShotManager.Tests.VideoDownscaler;

public class OutputPathResolverTests
{
    [Fact]
    public void Resolve_returns_downscaled_sibling_when_free()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ssm-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(dir, "movie.mp4");
            File.WriteAllText(src, "x");

            var result = OutputPathResolver.Resolve(src);

            Assert.Equal(Path.Combine(dir, "movie.downscaled.mp4"), result);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Resolve_appends_numeric_suffix_when_target_exists()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ssm-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(dir, "movie.mp4");
            File.WriteAllText(src, "x");
            File.WriteAllText(Path.Combine(dir, "movie.downscaled.mp4"), "x");
            File.WriteAllText(Path.Combine(dir, "movie.downscaled (1).mp4"), "x");

            var result = OutputPathResolver.Resolve(src);

            Assert.Equal(Path.Combine(dir, "movie.downscaled (2).mp4"), result);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Resolve_preserves_extension()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ssm-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(dir, "clip.mkv");
            File.WriteAllText(src, "x");

            var result = OutputPathResolver.Resolve(src);

            Assert.EndsWith(".mkv", result);
            Assert.Contains(".downscaled", result);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
