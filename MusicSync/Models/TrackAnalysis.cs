using System;
using System.Collections.Generic;
using System.Linq;

namespace ScreenShotManager.MusicSync.Models;

/// <summary>
/// Result of decoding a song once with ffmpeg: EBU R128 integrated loudness, true peak, and a
/// coarse waveform (one 0–255 peak per bucket) for the phone's seek bar.
/// </summary>
public sealed record TrackAnalysis(double LoudnessLufs, double PeakDb, byte[] Waveform)
{
    /// <summary>ReplayGain 2.0 reference level.</summary>
    public const double ReferenceLufs = -18.0;

    /// <summary>dB to apply so this song plays at the reference loudness.</summary>
    public double GainDb => Math.Round(ReferenceLufs - LoudnessLufs, 2);

    /// <summary>
    /// Album loudness = duration-weighted energy average of its tracks, so a quiet interlude
    /// keeps its intended level relative to the rest of the album.
    /// </summary>
    public static double? AlbumGainDb(IEnumerable<(double LoudnessLufs, long DurationMs)> tracks)
    {
        var list = tracks.Where(t => t.DurationMs > 0).ToList();
        if (list.Count == 0)
        {
            return null;
        }

        var totalMs = list.Sum(t => (double)t.DurationMs);
        var energy = list.Sum(t => t.DurationMs * Math.Pow(10, t.LoudnessLufs / 10)) / totalMs;
        return Math.Round(ReferenceLufs - 10 * Math.Log10(energy), 2);
    }
}
