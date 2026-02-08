using Core;

using Frontend;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Serilog;
using Serilog.Templates;
using Serilog.Templates.Themes;

using SharedLib.Converters;

using System;
using System.IO;
using System.Threading;

namespace BlazorServer;

public static class Program
{
    public static void Main(string[] args)
    {
        while (true)
        {
            bool shutdownRequested = false;

            try
            {
                Log.Information("[Program          ] Starting blazor server");
                var host = CreateApp(args);
                var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
                lifetime.ApplicationStopping.Register(() =>
                {
                    shutdownRequested = true;
                    Log.Warning("[Program          ] Graceful shutdown requested");
                });

                host.Run();
            }
            catch (Exception ex)
            {
                if (shutdownRequested)
                {
                    // We were stopping anyway; don't restart-loop just because Dispose threw.
                    Log.Error(ex, "[Program          ] Exception during shutdown; exiting without restart");
                    break;
                }

                Log.Fatal(ex, "[Program          ] Host crashed – restarting in 3s");
                Thread.Sleep(3000);
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }

    private static WebApplication CreateApp(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders().AddSerilog();

        ConfigureServices(builder.Configuration, builder.Services);

        return ConfigureApp(builder, builder.Environment);
    }

    private static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
    {
        ILoggerFactory logFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders().AddSerilog();
        });

        services.AddLogging(builder =>
        {
            LoggerSink sink = new();
            builder.Services.AddSingleton(sink);

            const string outputTemplate = "[{@t:HH:mm:ss:fff} {@l:u1}] {#if Length(SourceContext) > 0}[{Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),-17}] {#end}{@m}\n{@x}";
            //const string outputTemplate = "[{@t:HH:mm:ss:fff} {@l:u1}] {SourceContext}] {@m}\n{@x}";

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .Enrich.FromLogContext()
                .WriteTo.Sink(sink)
                .WriteTo.File(new ExpressionTemplate(outputTemplate),
                    "out.log",
                    rollingInterval: RollingInterval.Day)
                .WriteTo.Debug(new ExpressionTemplate(outputTemplate))
                .WriteTo.Console(new ExpressionTemplate(outputTemplate, theme: TemplateTheme.Literate))
                .CreateLogger();

            builder.Services.AddSingleton<Microsoft.Extensions.Logging.ILogger>(logFactory.CreateLogger(string.Empty));
        });

        Microsoft.Extensions.Logging.ILogger log = logFactory.CreateLogger("Program");

        if (log.IsEnabled(LogLevel.Information))
        {
            log.LogInformation("{Language} {Timestamp}",
                Thread.CurrentThread.CurrentCulture.TwoLetterISOLanguageName,
                DateTimeOffset.Now);
        }

        services.AddStartupConfigurations(configuration);

        services.AddWoWProcess(log);

        services.AddCoreBase(log);

        if (AddonConfig.Exists() && FrameConfig.Exists())
        {
            services.AddCoreNormal(log);
        }
        else
        {
            services.AddCoreConfiguration(log);
        }

        services.AddFrontend();

        services.AddCoreFrontend();

        services.AddSingleton(provider =>
            provider.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions);

        services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
            options.SerializerOptions.Converters.Add(new Vector3Converter());
            options.SerializerOptions.Converters.Add(new Vector4Converter());
        });

        services.Configure<HostOptions>(o =>
        {
            o.ShutdownTimeout = TimeSpan.FromSeconds(1);
        });

        // Register mDNS advertising service for http://wowbot.local access
        if(Environment.GetEnvironmentVariable("USE_MDNS") != null)
        {
            services.AddHostedService<MdnsAdvertisingService>();
        }

        services.AddControllers().AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
            options.JsonSerializerOptions.Converters.Add(new Vector3Converter());
            options.JsonSerializerOptions.Converters.Add(new Vector4Converter());
        });

        services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true });
    }

    private static WebApplication ConfigureApp(WebApplicationBuilder builder, IWebHostEnvironment env)
    {
        WebApplication app = builder.Build();

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Error");
        }

        app.UseStaticFiles();

        app.UseCustomStaticFiles(env);

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode()
            .AddAdditionalAssemblies(typeof(Frontend._Imports).Assembly);

        app.UseRouting();

        app.UseAntiforgery();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });

        return app;
    }

}