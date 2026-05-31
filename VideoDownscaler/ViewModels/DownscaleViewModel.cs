using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using ScreenShotManager.VideoDownscaler.Models;
using ScreenShotManager.VideoDownscaler.Services.Interfaces;

namespace ScreenShotManager.VideoDownscaler.ViewModels;

/// <summary>One row in the downscale activity list, bindable from a future UI.</summary>
public sealed class DownscaleJobViewModel : INotifyPropertyChanged
{
    private string status;

    public DownscaleJobViewModel(string fileName, string status)
    {
        FileName = fileName;
        this.status = status;
    }

    public string FileName { get; }

    public string Status
    {
        get => status;
        set
        {
            if (status == value)
            {
                return;
            }

            status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Observable view over <see cref="IDownscaleService"/> activity. Subscribes to the service's
/// events and marshals updates onto the UI dispatcher so views can data-bind directly.
/// </summary>
public sealed class DownscaleViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IDownscaleService service;
    private int activeCount;

    public DownscaleViewModel(IDownscaleService service)
    {
        this.service = service;
        this.service.JobStarted += OnJobStarted;
        this.service.JobFinished += OnJobFinished;
    }

    public ObservableCollection<DownscaleJobViewModel> Jobs { get; } = new();

    /// <summary>Number of jobs currently queued or running. Useful for tray tooltip/badge.</summary>
    public int ActiveCount
    {
        get => activeCount;
        private set
        {
            if (activeCount == value)
            {
                return;
            }

            activeCount = value;
            Raise(nameof(ActiveCount));
            Raise(nameof(IsBusy));
        }
    }

    public bool IsBusy => ActiveCount > 0;

    private void OnJobStarted(object? sender, DownscaleRequest request) =>
        OnUi(() =>
        {
            var name = System.IO.Path.GetFileName(request.SourcePath);
            Jobs.Insert(0, new DownscaleJobViewModel(name, "Processing…"));
            ActiveCount++;
        });

    private void OnJobFinished(object? sender, DownscaleJobResult result) =>
        OnUi(() =>
        {
            var name = System.IO.Path.GetFileName(result.SourcePath);
            var label = result.Outcome switch
            {
                DownscaleOutcome.Completed =>
                    $"Done → {result.TargetResolution}",
                DownscaleOutcome.Skipped => $"Skipped ({result.Message})",
                _ => $"Failed ({result.Message})",
            };

            foreach (var job in Jobs)
            {
                if (job.FileName == name && job.Status == "Processing…")
                {
                    job.Status = label;
                    break;
                }
            }

            if (ActiveCount > 0)
            {
                ActiveCount--;
            }
        });

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Dispose()
    {
        service.JobStarted -= OnJobStarted;
        service.JobFinished -= OnJobFinished;
    }
}
