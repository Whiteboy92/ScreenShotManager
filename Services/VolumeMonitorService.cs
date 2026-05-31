using System;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using ScreenShotManager.Services.Interfaces;

namespace ScreenShotManager.Services
{
    public class InputVolumeMonitorService : IVolumeMonitorService
    {
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _monitoringTask;
        private const int CheckIntervalSeconds = 15;
        private const float TargetVolume = 1.0f; // 100% = 1.0

        public Task StartMonitoringAsync(CancellationToken cancellationToken = default)
        {
            if (_monitoringTask is { IsCompleted: false })
            {
                return Task.CompletedTask;
            }

            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _monitoringTask = Task.Run(() => MonitorVolumeAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
            
            return Task.CompletedTask;
        }

        public void StopMonitoring()
        {
            _cancellationTokenSource?.Cancel();
        }

        private async Task MonitorVolumeAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    CheckAndSetInputVolume();
                }
                catch (Exception ex)
                {
                    // Log the exception if you have logging set up
                    System.Diagnostics.Debug.WriteLine($"Error checking input volume: {ex.Message}");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(CheckIntervalSeconds), cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    // Expected when cancellation is requested
                    break;
                }
            }
        }

        private void CheckAndSetInputVolume()
        {
            using var deviceEnumerator = new MMDeviceEnumerator();
            
            // Get all active capture (input) devices
            var captureDevices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

            foreach (var device in captureDevices)
            {
                try
                {
                    // Get the default capture device (microphone)
                    var defaultDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                    
                    // Only process the default device to avoid changing all microphones
                    if (device.ID != defaultDevice.ID)
                    {
                        continue;
                    }

                    var volume = device.AudioEndpointVolume;
                    float currentVolume = volume.MasterVolumeLevelScalar;

                    // Check if volume is not at 100%
                    if (Math.Abs(currentVolume - TargetVolume) > 0.01f)
                    {
                        volume.MasterVolumeLevelScalar = TargetVolume;
                        System.Diagnostics.Debug.WriteLine($"Input volume adjusted from {currentVolume * 100:F0}% to 100% for device: {device.FriendlyName}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error setting volume for device {device.FriendlyName}: {ex.Message}");
                }
            }
        }
    }
}