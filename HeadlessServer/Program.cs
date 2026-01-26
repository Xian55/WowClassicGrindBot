using CommandLine;

using Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Templates;
using Serilog.Templates.Themes;

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
            const string outputTemplate = "[{@t:HH:mm:ss:fff} {@l:u1}] {#if Length(SourceContext) > 0}[{Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),-17}] {#end}{@m}\n{@x}";
            //const string outputTemplate = "[{@t:HH:mm:ss:fff} {@l:u1}] {SourceContext}] {@m}\n{@x}";

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .Enrich.FromLogContext()
                .WriteTo.File(new ExpressionTemplate(outputTemplate),
                    path: "headless_out.log",
                    rollingInterval: RollingInterval.Day)
                .WriteTo.Debug(new ExpressionTemplate(outputTemplate))
                .WriteTo.Console(new ExpressionTemplate(outputTemplate, theme: TemplateTheme.Literate))
                .CreateLogger();

            builder.Services.AddSingleton<Microsoft.Extensions.Logging.ILogger>(logFactory.CreateLogger(string.Empty));
            builder.AddSerilog();
        });

        ILogger<Program> log = logFactory.CreateLogger<Program>();

        log.LogInformation($"Hosting environment: {environmentName ?? "Production"}");

        log.LogInformation(
            $"{Thread.CurrentThread.CurrentCulture.TwoLetterISOLanguageName} " +
            $"{DateTimeOffset.Now}");

        ParserResult<RunOptions> options =
            Parser.Default.ParseArguments<RunOptions>(args).WithNotParsed(errors =>
        {
            foreach (Error? e in errors)
            {
                log.LogError($"{e}");
            }
        });

        if (options.Tag == ParserResultType.NotParsed)
        {
            goto Exit;
        }

        services.AddSingleton<RunOptions>(options.Value);

        services.AddStartupConfigFactories();

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
            headlessServer.RunLoadOnly(options);
            Environment.Exit(0);
        }
        else
        {
            headlessServer.Run(options);
        }

    Exit:
        Console.ReadKey();
    }

    private static bool ConfigureServices(
        Microsoft.Extensions.Logging.ILogger log,
        IServiceCollection services)
    {
        if (!services.AddWoWProcess(log))
            return false;

        services.AddCoreBase();
        services.AddCoreNormal(log);

        return true;
    }
}