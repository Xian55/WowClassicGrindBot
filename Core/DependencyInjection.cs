using Core.Addon;
using Core.Database;
using Core.Extensions;
using Core.Goals;
using Core.Session;

using Game;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PPather;

using SharedLib;
using SharedLib.NpcFinder;

using SixLabors.ImageSharp;

using System;
using System.Threading;

using WinAPI;

namespace Core;

public static class DependencyInjection
{
    public static IServiceCollection AddAddonComponents(
        this IServiceCollection s)
    {
        s.ForwardSingleton<PlayerReader, IReader>();
        s.ForwardSingleton<PlayerReader, IMouseOverReader>();

        s.ForwardSingleton<AddonReader, IAddonReader>();

        s.ForwardSingleton<AddonBits, IReader>();
        s.ForwardSingleton<AddonBits, IGameMenuWindowShown>();

        s.ForwardSingleton<SpellInRange, IReader>();
        s.ForwardSingleton<BuffStatus<IPlayer>, IReader>(x => new(41));
        s.ForwardSingleton<TargetDebuffStatus, IReader>();
        s.ForwardSingleton<BuffStatus<IFocus>, IReader>(x => new(91));
        s.ForwardSingleton<Stance, IReader>();

        s.ForwardSingleton<CombatLog, IReader>();
        s.ForwardSingleton<EquipmentReader, IReader>();
        s.ForwardSingleton<BagReader, IReader>();
        s.ForwardSingleton<GossipReader, IReader>();
        s.ForwardSingleton<SpellBookReader, IReader>();
        s.ForwardSingleton<TalentReader, IReader>();
        s.ForwardSingleton<ChatReader, IReader>();

        s.ForwardSingleton<ActionBarCostReader, IReader>();
        s.ForwardSingleton<ActionBarCooldownReader, IReader>();

        s.ForwardSingleton<ActionBarBits<ICurrentAction>, IReader>(
            x => new(25, 26, 27, 28, 29));
        s.ForwardSingleton<ActionBarBits<IUsableAction>, IReader>(
            x => new(30, 31, 32, 33, 34));

        s.ForwardSingleton<AuraTimeReader<IPlayerBuffTimeReader>, IReader>(
            x => new(79, 80));
        s.ForwardSingleton<AuraTimeReader<ITargetDebuffTimeReader>, IReader>(
            x => new(81, 82));
        s.ForwardSingleton<AuraTimeReader<ITargetBuffTimeReader>, IReader>(
            x => new(83, 84));
        s.ForwardSingleton<AuraTimeReader<IFocusBuffTimeReader>, IReader>(
            x => new(92, 93));
        s.ForwardSingleton<AuraTimeReader<IPlayerDebuffTimeReader>, IReader>(
            x => new(104, 105));

        return s;
    }

    public static IServiceCollection AddStartupIoC(
        this IServiceCollection s, IServiceProvider sp)
    {
        s.ForwardSingleton<ILoggerFactory>(sp);
        s.ForwardSingleton<ILogger>(sp);

        s.AddLogging();

        s.ForwardSingleton<CancellationTokenSource>(sp);

        s.ForwardSingleton<WowProcessInput>(sp);
        s.ForwardSingleton<IMouseInput>(sp);
        s.ForwardSingleton<IMouseOverReader>(sp);

        s.ForwardSingleton<NpcNameFinder>(sp);
        s.ForwardSingleton<NpcNameTargetingLocations>(sp);
        s.ForwardSingleton<IWowScreen>(sp);

        s.ForwardSingleton<IPPather>(sp);
        s.ForwardSingleton<ExecGameCommand>(sp);

        s.ForwardSingleton<Wait>(sp);

        s.ForwardSingleton<DataConfig>(sp);

        s.ForwardSingleton<AreaDB>(sp);
        s.ForwardSingleton<WorldMapAreaDB>(sp);
        s.ForwardSingleton<ItemDB>(sp);
        s.ForwardSingleton<CreatureDB>(sp);
        s.ForwardSingleton<FactionTemplateDB>(sp);
        s.ForwardSingleton<SpellDB>(sp);
        s.ForwardSingleton<TalentDB>(sp);

        s.ForwardSingleton<AddonReader>(sp);
        s.ForwardSingleton<PlayerReader>(sp);

        s.ForwardSingleton<AddonBits>(sp);
        s.ForwardSingleton<IGameMenuWindowShown>(sp);

        s.ForwardSingleton<SpellInRange>(sp);
        s.ForwardSingleton<BuffStatus<IPlayer>>(sp);
        s.ForwardSingleton<TargetDebuffStatus>(sp);
        s.ForwardSingleton<BuffStatus<IFocus>>(sp);
        s.ForwardSingleton<Stance>(sp);

        s.ForwardSingleton<IScreenCapture>(sp);
        s.ForwardSingleton<SessionStat>(sp);
        s.ForwardSingleton<IGrindSessionDAO>(sp);

        // Addon Components
        s.ForwardSingleton<CombatLog>(sp);
        s.ForwardSingleton<EquipmentReader>(sp);
        s.ForwardSingleton<BagReader>(sp);
        s.ForwardSingleton<GossipReader>(sp);
        s.ForwardSingleton<SpellBookReader>(sp);
        s.ForwardSingleton<TalentReader>(sp);
        s.ForwardSingleton<ChatReader>(sp);

        s.ForwardSingleton<ActionBarCostReader>(sp);
        s.ForwardSingleton<ActionBarCooldownReader>(sp);

        s.ForwardSingleton<ActionBarBits<ICurrentAction>>(sp);
        s.ForwardSingleton<ActionBarBits<IUsableAction>>(sp);

        s.ForwardSingleton<AuraTimeReader<IPlayerBuffTimeReader>>(sp);
        s.ForwardSingleton<AuraTimeReader<IPlayerDebuffTimeReader>>(sp);
        s.ForwardSingleton<AuraTimeReader<ITargetDebuffTimeReader>>(sp);
        s.ForwardSingleton<AuraTimeReader<ITargetBuffTimeReader>>(sp);
        s.ForwardSingleton<AuraTimeReader<IFocusBuffTimeReader>>(sp);

        return s;
    }

    public static IServiceCollection AddCoreFrontend(
        this IServiceCollection s)
    {
        s.AddSingleton<WApi>();
        s.AddSingleton<FrontendUpdate>();

        return s;
    }

    public static IServiceCollection AddCoreConfiguration(
        this IServiceCollection s, ILogger log)
    {
        s.AddSingleton<IAddonDataProvider>(x => GetAddonDataProvider(x.GetRequiredService<IServiceProvider>(), log));
        s.AddSingleton<IBotController, ConfigBotController>();
        s.AddSingleton<IAddonReader, ConfigAddonReader>();

        return s;
    }

    public static IServiceCollection AddCoreNormal(
        this IServiceCollection s, ILogger log)
    {
        s.AddSingleton<IScreenCapture>(x =>
            GetScreenCapture(x.GetRequiredService<IServiceProvider>(), log));

        s.AddSingleton<IPathVizualizer>(x =>
            GetPathVizualizer(x.GetRequiredService<IServiceProvider>(), log));

        s.AddSingleton<IPPather>(x =>
            GetPather(x.GetRequiredService<IServiceProvider>(), log));

        s.AddSingleton<PPatherService>(x =>
        {
            var loggerFactory = x.GetRequiredService<ILoggerFactory>();
            var serviceLogger = loggerFactory.CreateLogger<PPatherService>();
            var dataConfig = x.GetRequiredService<DataConfig>();
            var worldMapAreaDB = x.GetRequiredService<WorldMapAreaDB>();
            return new PPatherService(serviceLogger, dataConfig, worldMapAreaDB);
        });

        s.AddSingleton<IAddonDataProvider>(x =>
            GetAddonDataProvider(x.GetRequiredService<IServiceProvider>(), log));

        s.AddSingleton<MinimapNodeFinder>();

        s.AddSingleton<SessionStat>();
        s.AddSingleton<IGrindSessionDAO, LocalGrindSessionDAO>();
        s.AddSingleton<LevelTracker>();
        s.AddSingleton<TimeToKill>();

        s.AddSingleton<AreaDB>();
        s.AddSingleton<WorldMapAreaDB>();
        s.AddSingleton<ItemDB>();
        s.AddSingleton<CreatureDB>();
        s.AddSingleton<FactionTemplateDB>();
        s.AddSingleton<SpellDB>();
        s.AddSingleton<TalentDB>();

        s.AddAddonComponents();

        s.AddSingleton<IBotController, BotController>();

        return s;
    }

    public static IServiceCollection AddCoreBase(this IServiceCollection s)
    {
        s.AddSingleton<ManualResetEventSlim>(x => new(false));
        s.AddSingleton<Wait>();

        s.AddSingleton<StartupClientVersion>();
        s.AddSingleton<DataConfig>(x => DataConfig.Load(
            x.GetRequiredService<StartupClientVersion>().Path));

        s.ForwardSingleton<IWowScreen, IScreenImageProvider, IMinimapImageProvider, WowScreenDXGI>();

        s.ForwardSingleton<WowProcessInput, IMouseInput>();

        s.AddSingleton<ExecGameCommand>();

        s.AddSingleton<DataFrame[]>(x => FrameConfig.LoadFrames());
        s.AddSingleton<FrameConfigurator>();

        s.AddSingleton<INpcResetEvent, NpcResetEvent>();
        s.AddSingleton<NpcNameFinder>();

        s.AddSingleton<NpcNameTargetingLocations>();

        return s;
    }


    public static bool AddWoWProcess(
        this IServiceCollection services, ILogger log)
    {
        services.AddSingleton<CancellationTokenSource>();
        services.AddSingleton<WowProcess>();
        services.AddSingleton<AddonConfigurator>();

        var sp = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true });

        WowProcess process = sp.GetRequiredService<WowProcess>();
        log.LogInformation($"Pid: {process.Id}");
        log.LogInformation($"Version: {process.FileVersion}");

        services.AddSingleton<Version>(x => process.FileVersion);

        AddonConfigurator configurator = sp.GetRequiredService<AddonConfigurator>();
        Version? installVersion = configurator.GetInstallVersion();
        log.LogInformation($"Addon version: {installVersion}");

        if (configurator.IsDefault() || installVersion == null)
        {
            // At this point the webpage never loads so fallback to configuration page
            configurator.Delete();
            FrameConfig.Delete();

            log.LogError($"{nameof(AddonConfig)} doesn't exists or addon not installed yet!");
            return false;
        }

        NativeMethods.GetWindowRect(process.MainWindowHandle, out Rectangle rect);
        if (!FrameConfig.Exists())
        {
            log.LogError($"{nameof(FrameConfig)} doesn't exists!");

            return false;
        }

        if (!FrameConfig.IsValid(rect, installVersion))
        {
            // At this point the webpage never loads so fallback to configuration page
            FrameConfig.Delete();

            log.LogError($"{nameof(FrameConfig)} window rect is different then config!");
            log.LogError($"{nameof(FrameConfig)} {rect}");
            log.LogError($"{nameof(FrameConfig)} {installVersion}");
            log.LogError($"{nameof(FrameConfig)} {FrameConfig.Load()}");

            return false;
        }


        return true;
    }


    private static IScreenCapture GetScreenCapture(
        IServiceProvider sp, ILogger log)
    {
        var spd = sp.GetRequiredService<IOptions<StartupConfigDiagnostics>>().Value;
        var globalLogger = sp.GetRequiredService<ILogger>();
        var logger = sp.GetRequiredService<ILogger<ScreenCapture>>();
        var dataConfig = sp.GetRequiredService<DataConfig>();
        var cts = sp.GetRequiredService<CancellationTokenSource>();
        var screen = sp.GetRequiredService<IWowScreen>();

        IScreenCapture value = spd.Enabled
            ? new ScreenCapture(logger, dataConfig, cts, screen)
            : new NoScreenCapture(globalLogger, dataConfig);

        log.LogInformation(value.GetType().Name);

        return value;
    }

    private static IAddonDataProvider GetAddonDataProvider(
        IServiceProvider sp, ILogger log)
    {
        var scr = sp.GetRequiredService<IOptions<StartupConfigReader>>().Value;
        var screen = sp.GetRequiredService<IWowScreen>();

        IAddonDataProvider value = scr.ReaderType switch
        {
            AddonDataProviderType.DXGI =>
                (IAddonDataProvider)screen,
            _ => throw new NotImplementedException(),
        };

        log.LogInformation(value.GetType().Name);
        return value;
    }

    private static IPPather GetPather(IServiceProvider sp, ILogger logger)
    {
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var scp = sp.GetRequiredService<IOptions<StartupConfigPathing>>().Value;
        var dataConfig = sp.GetRequiredService<DataConfig>();
        var worldMapAreaDB = sp.GetRequiredService<WorldMapAreaDB>();
        var pathViz = sp.GetRequiredService<IPathVizualizer>();

        bool failed = false;
        if (scp.Type == StartupConfigPathing.Types.RemoteV3)
        {
            var remoteLogger = loggerFactory.CreateLogger<RemotePathingAPIV3>();
            RemotePathingAPIV3 api = new(
                pathViz,
                remoteLogger,
                scp.hostv3, scp.portv3, worldMapAreaDB);
            if (api.PingServer())
            {
                logger.LogInformation(
                    $"Using {StartupConfigPathing.Types.RemoteV3}({api.GetType().Name}) " +
                    $"{scp.hostv3}:{scp.portv3}");
                return api;
            }
            api.Dispose();
            failed = true;
        }

        if (scp.Type == StartupConfigPathing.Types.RemoteV1 || failed)
        {
            var remoteLogger = loggerFactory.CreateLogger<RemotePathingAPI>();
            RemotePathingAPI api = new(remoteLogger, scp.hostv1, scp.portv1);
            if (api.PingServer())
            {
                if (scp.Type == StartupConfigPathing.Types.RemoteV3)
                {
                    logger.LogWarning(
                        $"Unavailable {StartupConfigPathing.Types.RemoteV3} " +
                        $"{scp.hostv3}:{scp.portv3} - Fallback to " +
                        $"{StartupConfigPathing.Types.RemoteV1}");
                }

                logger.LogInformation(
                    $"Using {StartupConfigPathing.Types.RemoteV1}({api.GetType().Name}) " +
                    $"{scp.hostv1}:{scp.portv1}");
                return api;
            }
        }

        if (scp.Type != StartupConfigPathing.Types.Local)
        {
            logger.LogWarning($"{scp.Type} not available!");
        }

        var service = sp.GetRequiredService<PPatherService>();
        var pathingLogger = loggerFactory.CreateLogger<LocalPathingApi>();

        LocalPathingApi localApi = new(pathingLogger, service);
        logger.LogInformation(
            $"Using {StartupConfigPathing.Types.Local}({localApi.GetType().Name})");

        return localApi;
    }

    private static IPathVizualizer GetPathVizualizer(IServiceProvider sp, ILogger logger)
    {
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var remoteLogger = loggerFactory.CreateLogger<RemotePathingAPI>();

        var scp = sp.GetRequiredService<IOptions<StartupConfigPathing>>().Value;

        if (!scp.PathVisualizer)
        {
            return new NoPathVisualizer();
        }

        RemotePathingAPI? api = new(remoteLogger, scp.hostv1, scp.portv1);
        if (!api.PingServer())
        {
            api.Dispose();
            api = null;
        }
        else
        {
            logger.LogInformation(
                $"Found PathViz {StartupConfigPathing.Types.RemoteV1}({api.GetType().Name}) " +
                $"{scp.hostv1}:{scp.portv1}");
        }

        return api ?? (IPathVizualizer)new NoPathVisualizer();
    }
}
