using Microsoft.Extensions.DependencyInjection;
using ScreenShotManager.MusicSync.Services.Interfaces;
using ScreenShotManager.VideoDownscaler.Services;
using ScreenShotManager.VideoDownscaler.Services.Interfaces;

namespace ScreenShotManager.MusicSync.Services;

/// <summary>
/// DI registration for the "Music Sync" feature. Call from composition root.
/// </summary>
public static class MusicSyncServiceCollectionExtensions
{
    public static IServiceCollection AddMusicSyncFeature(this IServiceCollection services)
    {
        services.AddSingleton<ITrackMetadataReader, TrackMetadataReader>();
        services.AddSingleton<ITrackCache, TrackCache>();
        services.AddSingleton<IToolPathProvider, ToolPathProvider>();
        services.AddSingleton<ITrackAnalyzer, TrackAnalyzer>();
        services.AddSingleton<IMusicLibraryService, MusicLibraryService>();
        services.AddSingleton<ICertificateProvider, CertificateProvider>();
        services.AddSingleton<IDeviceRegistry, DeviceRegistry>();
        services.AddSingleton<IMusicSyncServer, MusicSyncServer>();
        services.AddSingleton<IMdnsAdvertiser, MdnsAdvertiser>();
        return services;
    }
}
