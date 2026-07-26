using CommandLine;

using Core;

using Frontend;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Templates;
using Serilog.Templates.Themes;

using SharedLib.Logging;

namespace HeadlessServer;

public sealed class Program
{
    public static void Main(string[] args)
    {
        var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("headless_appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"headless_appsettings.{environmentName}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        IServiceCollection services = new ServiceCollection();

        ILoggerFactory logFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders().AddSerilog();
        });

        services.AddLogging(builder =>
        {
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .Enrich.FromLogContext()
                .Enrich.With<ShortSourceContextEnricher>()
                .WriteTo.File(new ExpressionTemplate(LogOutputTemplates.Default),
                    path: "headless_out.log",
                    rollingInterval: RollingInterval.Day)
                .WriteTo.Debug(new ExpressionTemplate(LogOutputTemplates.Default))
                .WriteTo.Console(new ExpressionTemplate(LogOutputTemplates.Default, theme: TemplateTheme.Literate))
                .CreateLogger();

            builder.Services.AddSingleton<Microsoft.Extensions.Logging.ILogger>(logFactory.CreateLogger(string.Empty));
            builder.AddSerilog();
        });

        ILogger<Program> log = logFactory.CreateLogger<Program>();

        if (log.IsEnabled(LogLevel.Information))
        {
            log.LogInformation($"Hosting environment: {environmentName ?? "Production"}");

            log.LogInformation(
                $"{Thread.CurrentThread.CurrentCulture.TwoLetterISOLanguageName} " +
                $"{DateTimeOffset.Now}");
        }

        // --help and --version come back as NotParsed too, but they are a satisfied
        // request rather than a failure and must not report one.
        bool helpOrVersion = false;

        ParserResult<RunOptions> options =
            Parser.Default.ParseArguments<RunOptions>(args).WithNotParsed(errors =>
        {
            foreach (Error? e in errors)
            {
                if (e is HelpRequestedError or HelpVerbRequestedError or VersionRequestedError)
                {
                    helpOrVersion = true;
                    continue;
                }

                log.LogError($"{e}");
            }
        });

        if (options.Tag == ParserResultType.NotParsed)
        {
            Environment.ExitCode = helpOrVersion ? 0 : 1;
            goto Exit;
        }

        services.AddSingleton<RunOptions>(options.Value);

        services.AddStartupConfigFactories();

        // Navmesh / follower tunables bind from the same configuration the host
        // already built (json + env + command line).
        services.Configure<SharedLib.NavmeshBakeOptions>(
            configuration.GetSection(SharedLib.NavmeshBakeOptions.Position));
        services.Configure<SharedLib.NavmeshQueryOptions>(
            configuration.GetSection(SharedLib.NavmeshQueryOptions.Position));
        services.Configure<SharedLib.SplineFollowerOptions>(
            configuration.GetSection(SharedLib.SplineFollowerOptions.Position));

        if (!FrameConfig.Exists() || !AddonConfig.Exists())
        {
            log.LogError($"Unable to run {nameof(HeadlessServer)} as crucial configuration files were missing!");
            log.LogWarning($"Please be sure, the following validated configuration files present next to the executable:");
            log.LogWarning($"{Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)}");
            log.LogWarning($"* {DataConfigMeta.DefaultFileName}");
            log.LogWarning($"* {FrameConfigMeta.DefaultFilename}");
            log.LogWarning($"* {AddonConfigMeta.DefaultFileName}");
            goto Exit;
        }

        if (!ConfigureServices(log, services))
        {
            goto Exit;
        }

        ServiceProvider provider = services
            .AddSingleton<HeadlessServer>()
            .BuildServiceProvider(new ServiceProviderOptions() { ValidateOnBuild = true });

        var logger =
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger>();

        AppDomain.CurrentDomain.UnhandledException += (object sender, UnhandledExceptionEventArgs args) =>
        {
            Exception e = (Exception)args.ExceptionObject;
            logger.LogError(e, e.Message);
        };

        HeadlessServer headlessServer = provider.GetRequiredService<HeadlessServer>();

        if (options.Value.LoadOnly)
        {
            bool success = headlessServer.RunLoadOnly(options);
            Environment.Exit(success ? 0 : 1);
        }
        else
        {
            headlessServer.Run(options);
        }

    Exit:
        // Keeps the window open when launched by double-click. Guarded because
        // ReadKey throws InvalidOperationException when stdin is redirected or no
        // console is attached, which turned `--help` and every parse error into an
        // unhandled crash instead of a clean exit. run.bat already ends with `pause`,
        // so nothing is lost when this is skipped.
        if (!Console.IsInputRedirected)
        {
            try
            {
                Console.ReadKey(intercept: true);
            }
            catch (InvalidOperationException)
            {
                // No console to read from - exit quietly.
            }
        }
    }

    private static bool ConfigureServices(
        Microsoft.Extensions.Logging.ILogger log,
        IServiceCollection services)
    {
        if (!services.AddWoWProcess(log))
            return false;

        services.AddCoreBase(log);
        services.AddCoreNormal(log);

        return true;
    }
}