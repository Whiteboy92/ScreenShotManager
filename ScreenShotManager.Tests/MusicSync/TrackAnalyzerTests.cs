using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ScreenShotManager.MusicSync.Models;
using ScreenShotManager.MusicSync.Services;
using ScreenShotManager.VideoDownscaler.Services;

namespace ScreenShotManager.Tests.MusicSync;

public class TrackAnalyzerTests
{
    private const string Ebur128Tail = """
        [Parsed_ebur128_0 @ 000001] Summary:

          Integrated loudness:
            I:         -14.2 LUFS
            Threshold: -24.5 LUFS

          Loudness range:
            LRA:         6.1 LU

          True peak:
            Peak:       -0.3 dBFS
        """;

    [Fact]
    public void Parses_ebur128_summary()
    {
        var (loudness, peak) = TrackAnalyzer.ParseLoudness("t: 0.1 M: -20 S: -20\n" + Ebur128Tail);
        Assert.Equal(-14.2, loudness);
        Assert.Equal(-0.3, peak);
    }

    [Fact]
    public void Silence_or_missing_summary_has_no_loudness()
    {
        Assert.Null(TrackAnalyzer.ParseLoudness("no summary here").Loudness);
        Assert.Null(TrackAnalyzer.ParseLoudness(Ebur128Tail.Replace("-14.2", "-70.0")).Loudness);
    }

    [Fact]
    public void Gain_targets_minus_18_lufs()
    {
        Assert.Equal(-3.8, new TrackAnalysis(-14.2, -0.3, Array.Empty<byte>()).GainDb);
        Assert.Equal(6.0, new TrackAnalysis(-24, -6, Array.Empty<byte>()).GainDb);
    }

    [Fact]
    public void Album_gain_is_energy_weighted_by_duration()
    {
        // Two equal-length tracks 10 dB apart: energy average ≈ 2.6 dB below the louder one.
        var gain = TrackAnalysis.AlbumGainDb(new[] { (-10.0, 60_000L), (-20.0, 60_000L) });
        Assert.Equal(-18 - (-12.6), gain!.Value, 1);
        Assert.Null(TrackAnalysis.AlbumGainDb(Array.Empty<(double, long)>()));
    }

    [Fact]
    public void Waveform_buckets_peaks_and_normalises_to_255()
    {
        // 600 samples: first half quiet (1000), second half loud (8000).
        var pcm = new byte[600 * 2];
        for (var i = 0; i < 600; i++)
        {
            var v = (short)(i < 300 ? 1000 : -8000);
            pcm[i * 2] = (byte)(v & 0xFF);
            pcm[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }

        var wave = TrackAnalyzer.BuildWaveform(pcm);

        Assert.Equal(MusicSyncConstants.WaveformPoints, wave.Length);
        Assert.Equal(32, wave[0]);   // 1000/8000*255
        Assert.Equal(255, wave[^1]);
        Assert.All(TrackAnalyzer.BuildWaveform(Array.Empty<byte>()), b => Assert.Equal(0, b));
    }

    /// <summary>Real ffmpeg run on a generated 1 kHz tone at -6 dBFS (≈ -9 LUFS mono).</summary>
    [Fact]
    public async Task Analyses_real_audio_with_bundled_ffmpeg()
    {
        var analyzer = new TrackAnalyzer(new ToolPathProvider());
        if (!analyzer.IsAvailable)
        {
            return; // ffmpeg not bundled in this checkout
        }

        using var temp = new TempFolder();
        var wav = Path.Combine(temp.Path, "tone.wav");
        WriteSineWav(wav, seconds: 4, amplitude: 0.5);

        var result = await analyzer.AnalyzeAsync(wav, CancellationToken.None);

        Assert.NotNull(result);
        Assert.InRange(result!.LoudnessLufs, -10.0, -8.0);
        Assert.InRange(result.PeakDb, -6.6, -5.4);
        Assert.Equal(MusicSyncConstants.WaveformPoints, result.Waveform.Length);
        Assert.True(result.Waveform.Skip(10).Take(280).All(b => b > 200), "steady tone should give a flat, full waveform");
    }

    private static void WriteSineWav(string path, int seconds, double amplitude)
    {
        const int rate = 44100;
        var samples = rate * seconds;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + samples * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);      // PCM
        writer.Write((short)1);      // mono
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(samples * 2);
        for (var i = 0; i < samples; i++)
        {
            writer.Write((short)(Math.Sin(2 * Math.PI * 1000 * i / rate) * amplitude * short.MaxValue));
        }
    }
}
