using System.Threading;
using System.Threading.Tasks;
using ScreenShotManager.MusicSync.Models;

namespace ScreenShotManager.MusicSync.Services.Interfaces;

/// <summary>Measures loudness and builds a waveform for one audio file (slow: decodes the whole song).</summary>
public interface ITrackAnalyzer
{
    bool IsAvailable { get; }

    /// <summary>Null when the file can't be decoded or is silent.</summary>
    Task<TrackAnalysis?> AnalyzeAsync(string path, CancellationToken cancellationToken);
}
