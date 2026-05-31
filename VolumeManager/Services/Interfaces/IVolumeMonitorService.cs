using System.Threading;
using System.Threading.Tasks;

namespace ScreenShotManager.VolumeManager.Services.Interfaces
{
    /// <summary>
    /// Service that monitors and maintains Windows input volume at 100%
    /// </summary>
    public interface IVolumeMonitorService
    {
        /// <summary>
        /// Starts monitoring the input volume every 15 seconds
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the monitoring operation</param>
        /// <returns>Task representing the asynchronous operation</returns>
        Task StartMonitoringAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops the volume monitoring
        /// </summary>
        void StopMonitoring();
    }
}