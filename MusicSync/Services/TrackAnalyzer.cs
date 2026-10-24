using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ScreenShotManager.MusicSync.Models;
using ScreenShotManager.MusicSync.Services.Interfaces;
using ScreenShotManager.VideoDownscaler.Services.Interfaces;

namespace ScreenShotManager.MusicSync.Services;

/// <summary>
/// One ffmpeg decode per song yields everything the phone needs for volume levelling and the
/// waveform seek bar: <c>ebur128</c> measures integrated loudness + true peak (logged to stderr)
/// and passes the audio through, which is then downsampled to 4 kHz mono PCM on stdout and
/// bucketed into <see cref="MusicSyncConstants.WaveformPoints"/> peaks.
/// </summary>
public sealed partial class TrackAnalyzer : ITrackAnalyzer
{
    private const int SampleRate = 4000;
    private readonly string ffmpegPath;

    public TrackAnalyzer(IToolPathProvider tools)
    {
        ffmpegPath = tools.FFmpegPath;
    }

    internal TrackAnalyzer(string ffmpegPath)
    {
        this.ffmpegPath = ffmpegPath;
    }

    public bool IsAvailable => File.Exists(ffmpegPath);

    public async Task<TrackAnalysis?> AnalyzeAsync(string path, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return null;
        }

        var args = "-hide_banner -nostats -nostdin " +
                   $"-i \"{path}\" -map 0:a:0 " +
                   $"-af \"ebur128=peak=true:framelog=verbose,aresample={SampleRate},aformat=sample_fmts=s16:channel_layouts=mono\" " +
                   "-f s16le pipe:1";

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8,
            },
        };

        if (!process.Start())
        {
            return null;
        }

        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal; // never compete with games/capture
        }
        catch
        {
            // Not fatal if the process already exited.
        }

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var pcm = new MemoryStream();
        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(pcm, cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw;
        }

        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            return null;
        }

        var (loudness, peak) = ParseLoudness(stderr);
        if (loudness == null)
        {
            return null;
        }

        return new TrackAnalysis(loudness.Value, peak ?? 0, BuildWaveform(pcm.GetBuffer().AsSpan(0, (int)pcm.Length)));
    }

    /// <summary>Reads "I: -14.2 LUFS" and the true-peak "Peak: -0.3 dBFS" from ebur128's summary.</summary>
    internal static (double? Loudness, double? PeakDb) ParseLoudness(string stderr)
    {
        var summary = stderr.LastIndexOf("Summary:", StringComparison.Ordinal);
        if (summary < 0)
        {
            return (null, null);
        }

        var text = stderr[summary..];
        var integrated = IntegratedRegex().Match(text);
        var peak = PeakRegex().Match(text);

        double? Parse(Match m) =>
            m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? v
                : null;

        var loudness = Parse(integrated);
        // ebur128 reports -70 LUFS (its gate floor) for silence; treat as "no usable loudness".
        if (loudness is <= -69.9)
        {
            loudness = null;
        }

        return (loudness, Parse(peak));
    }

    /// <summary>Max |sample| per bucket, scaled so the loudest bucket is 255.</summary>
    internal static byte[] BuildWaveform(ReadOnlySpan<byte> pcm16)
    {
        var points = MusicSyncConstants.WaveformPoints;
        var samples = pcm16.Length / 2;
        var result = new byte[points];
        if (samples == 0)
        {
            return result;
        }

        var peaks = new int[points];
        for (var i = 0; i < samples; i++)
        {
            var sample = Math.Abs((int)(short)(pcm16[i * 2] | (pcm16[i * 2 + 1] << 8)));
            var bucket = (int)((long)i * points / samples);
            if (sample > peaks[bucket])
            {
                peaks[bucket] = sample;
            }
        }

        var max = 0;
        foreach (var p in peaks)
        {
            max = Math.Max(max, p);
        }

        if (max == 0)
        {
            return result;
        }

        for (var i = 0; i < points; i++)
        {
            result[i] = (byte)Math.Round(peaks[i] * 255.0 / max);
        }

        return result;
    }

    [GeneratedRegex(@"I:\s+(-?\d+(?:\.\d+)?)\s+LUFS")]
    private static partial Regex IntegratedRegex();

    [GeneratedRegex(@"Peak:\s+(-?\d+(?:\.\d+)?|-inf)\s+dBFS")]
    private static partial Regex PeakRegex();
}
