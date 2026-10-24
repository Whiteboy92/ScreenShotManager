using System;
using System.Collections.Generic;
using ScreenShotManager.VideoDownscaler.Models;
using ScreenShotManager.VideoDownscaler.Services.Interfaces;
using ScreenShotManager.VideoDownscaler.ViewModels;

namespace ScreenShotManager.Tests.VideoDownscaler;

public class DownscaleViewModelTests
{
    private sealed class FakeService : IDownscaleService
    {
        public event EventHandler<DownscaleJobResult>? JobFinished;
        public event EventHandler<DownscaleRequest>? JobStarted;
        public event EventHandler<DownscaleProgress>? JobProgress;
        public event EventHandler<string>? Faulted;

        public void Start() { }
        public bool Enqueue(string filePath) => true;
        public void Stop() { }

        public void RaiseStarted(string path) => JobStarted?.Invoke(this, new DownscaleRequest(path));
        public void RaiseFinished(DownscaleJobResult r) => JobFinished?.Invoke(this, r);
        public bool HasSubscribers => JobStarted != null || JobFinished != null;

        // Silence unused-event warnings.
        public void Touch()
        {
            JobProgress?.Invoke(this, default);
            Faulted?.Invoke(this, "");
        }
    }

    [Fact]
    public void Started_job_is_inserted_at_top_and_marks_busy()
    {
        var svc = new FakeService();
        using var vm = new DownscaleViewModel(svc);

        svc.RaiseStarted(@"C:\v\a.mp4");
        svc.RaiseStarted(@"C:\v\b.mp4");

        Assert.Equal(2, vm.ActiveCount);
        Assert.True(vm.IsBusy);
        Assert.Equal("b.mp4", vm.Jobs[0].FileName);
        Assert.Equal("a.mp4", vm.Jobs[1].FileName);
        Assert.All(vm.Jobs, j => Assert.Equal("Processing…", j.Status));
    }

    [Fact]
    public void Completed_job_updates_status_and_count()
    {
        var svc = new FakeService();
        using var vm = new DownscaleViewModel(svc);

        svc.RaiseStarted(@"C:\v\a.mp4");
        svc.RaiseFinished(DownscaleJobResult.Completed(
            @"C:\v\a.mp4", new VideoResolution(1920, 1080), new VideoResolution(1280, 720)));

        Assert.Equal(0, vm.ActiveCount);
        Assert.False(vm.IsBusy);
        Assert.Equal("Done 1920x1080 → 1280x720", vm.Jobs[0].Status);
    }

    [Fact]
    public void Skipped_and_failed_jobs_get_labels()
    {
        var svc = new FakeService();
        using var vm = new DownscaleViewModel(svc);

        svc.RaiseStarted(@"C:\v\s.mp4");
        svc.RaiseStarted(@"C:\v\f.mp4");
        svc.RaiseFinished(DownscaleJobResult.Skipped(@"C:\v\s.mp4", new VideoResolution(426, 240), "tiny"));
        svc.RaiseFinished(DownscaleJobResult.Failed(@"C:\v\f.mp4", "boom"));

        Assert.Equal("Failed (boom)", vm.Jobs[0].Status);
        Assert.Equal("Skipped (tiny)", vm.Jobs[1].Status);
    }

    [Fact]
    public void Finished_updates_only_one_processing_row_with_same_name()
    {
        var svc = new FakeService();
        using var vm = new DownscaleViewModel(svc);

        svc.RaiseStarted(@"C:\one\a.mp4");
        svc.RaiseStarted(@"C:\two\a.mp4");
        svc.RaiseFinished(DownscaleJobResult.Failed(@"C:\one\a.mp4", "x"));

        Assert.Equal(1, vm.ActiveCount);
        Assert.Single(vm.Jobs, j => j.Status == "Processing…");
    }

    [Fact]
    public void ActiveCount_never_goes_negative()
    {
        var svc = new FakeService();
        using var vm = new DownscaleViewModel(svc);

        svc.RaiseFinished(DownscaleJobResult.Failed(@"C:\v\orphan.mp4", "x"));

        Assert.Equal(0, vm.ActiveCount);
        Assert.Empty(vm.Jobs);
    }

    [Fact]
    public void Raises_property_changed_for_count_and_busy()
    {
        var svc = new FakeService();
        using var vm = new DownscaleViewModel(svc);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        svc.RaiseStarted(@"C:\v\a.mp4");

        Assert.Equal(new[] { nameof(DownscaleViewModel.ActiveCount), nameof(DownscaleViewModel.IsBusy) }, changed);
    }

    [Fact]
    public void Dispose_unsubscribes_from_service()
    {
        var svc = new FakeService();
        var vm = new DownscaleViewModel(svc);
        Assert.True(svc.HasSubscribers);

        vm.Dispose();

        Assert.False(svc.HasSubscribers);
    }

    [Fact]
    public void JobViewModel_raises_only_on_actual_change()
    {
        var job = new DownscaleJobViewModel("a.mp4", "Processing…");
        var count = 0;
        job.PropertyChanged += (_, e) =>
        {
            Assert.Equal(nameof(DownscaleJobViewModel.Status), e.PropertyName);
            count++;
        };

        job.Status = "Processing…";
        job.Status = "Done";
        job.Status = "Done";

        Assert.Equal(1, count);
        Assert.Equal("Done", job.Status);
    }
}
