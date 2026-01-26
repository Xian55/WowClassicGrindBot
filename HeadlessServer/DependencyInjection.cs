using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using SharedLib;

namespace HeadlessServer;
public static class DependencyInjection
{
    public static void AddStartupConfigFactories(this IServiceCollection services)
    {
        services.AddSingleton<IOptions<StartupConfigPathing>>(StartupConfigPathingFactory);
        services.AddSingleton<IOptions<StartupConfigReader>>(StartupConfigReaderFactory);
        services.AddSingleton<IOptions<StartupConfigPid>>(StartupConfigPidFactory);
        services.AddSingleton<IOptions<StartupConfigDiagnostics>>(StartupConfigDiagnosticsFactory);
        services.AddSingleton<IOptions<StartupConfigNpcOverlay>>(StartupConfigNpcOverlayFactory);
    }

    private static IOptions<StartupConfigPathing> StartupConfigPathingFactory(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<RunOptions>();

        return Options.Create<StartupConfigPathing>(
        new(options.Mode.ToString()!,
            options.Hostv1!, options.Portv1,
            options.Hostv3!, options.Portv3,
            options.PathVisualizer));
    }

    private static IOptions<StartupConfigReader> StartupConfigReaderFactory(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<RunOptions>();

        return Options.Create<StartupConfigReader>(
            new() { Type = options.Reader.ToString() });
    }

    private static IOptions<StartupConfigPid> StartupConfigPidFactory(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<RunOptions>();

        return Options.Create<StartupConfigPid>(
            new() { Id = options.Pid });
    }

    private static IOptions<StartupConfigDiagnostics> StartupConfigDiagnosticsFactory(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<RunOptions>();

        return Options.Create<StartupConfigDiagnostics>(
            new() { Enabled = options.Diagnostics });
    }

    private static IOptions<StartupConfigNpcOverlay> StartupConfigNpcOverlayFactory(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<RunOptions>();

        return Options.Create<StartupConfigNpcOverlay>(
            new()
            {
                Enabled = options.OverlayEnabled,
                ShowTargeting = options.OverlayTargeting,
                ShowSkinning = options.OverlaySkinning,
                ShowTargetVsAdd = options.OverlayTargetVsAdd,
            });
    }
}
