using System;
using ScreenShotManager.VideoDownscaler.Models;

namespace ScreenShotManager.VideoDownscaler.Services.Interfaces;

/// <summary>
/// Background queue that downscales videos sequentially without blocking the UI thread.
/// </summary>
public interface IDownscaleService
{
    /// <summary>Raised (on a background thread) when a job reaches a terminal state.</summary>
    event EventHandler<DownscaleJobResult>? JobFinished;

    /// <summary>Raised when a job is about to start encoding.</summary>
    event EventHandler<DownscaleRequest>? JobStarted;

    /// <summary>Starts the background worker. Idempotent.</summary>
    void Start();

    /// <summary>Enqueues one file for downscaling. Returns false if the path is unsupported.</summary>
    bool Enqueue(string filePath);

    /// <summary>Signals the worker to drain and stop.</summary>
    void Stop();
}
