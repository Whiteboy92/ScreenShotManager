using Microsoft.Extensions.DependencyInjection;
using ScreenShotManager.Services.Interfaces;
using ScreenShotManager.ViewModels;

namespace ScreenShotManager.Services.Downscaling;

/// <summary>
/// DI registration for the "Downscale Video" feature. Call from composition root.
/// </summary>
public static class DownscaleServiceCollectionExtensions
{
    public static IServiceCollection AddDownscaleFeature(this IServiceCollection services)
    {
        services.AddSingleton<IToolPathProvider, ToolPathProvider>();
        services.AddSingleton<IFFprobeService, FFprobeService>();
        services.AddSingleton<IFFmpegService, FFmpegService>();
        services.AddSingleton<IDownscaleService, DownscaleService>();
        services.AddSingleton<IDownscaleIpcServer, DownscaleIpcServer>();
        services.AddSingleton<IDownscaleIpcClient, DownscaleIpcClient>();
        services.AddSingleton<IContextMenuService, ContextMenuService>();
        services.AddSingleton<DownscaleViewModel>();
        return services;
    }
}
