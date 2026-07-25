using Core.Database;

using Frontend;

using MatBlazor;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

using PPather;

using Serilog;
using Serilog.Events;
using Serilog.Templates;
using Serilog.Templates.Themes;

using SharedLib;
using SharedLib.Logging;
using SharedLib.Converters;

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace PathingAPI;

public sealed class Startup
{
    private readonly IConfiguration configuration;

    public Startup(IConfiguration configuration)
    {
        this.configuration = configuration;
    }

    // This method gets called by the runtime. Use this method to add services to the container.
    // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            PathingAPILoggerSink sink = new();
            builder.Services.AddSingleton(sink);

            // Logging:PatherDebug=true opens the per-query navmesh diagnostics
            // (corridor legs, tile bakes) without recompiling.
            LogEventLevel minimum =
                configuration.GetValue<bool>("Logging:PatherDebug")
                ? LogEventLevel.Debug
                : LogEventLevel.Information;

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(minimum)
                //.MinimumLevel.Verbose()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.With<ShortSourceContextEnricher>()
                .WriteTo.Sink(sink)
                .WriteTo.File(new ExpressionTemplate(LogOutputTemplates.Default),
                    "out.log",
                    rollingInterval: RollingInterval.Day)
                .WriteTo.Debug(new ExpressionTemplate(LogOutputTemplates.Default))
                .WriteTo.Console(new ExpressionTemplate(LogOutputTemplates.Default, theme: TemplateTheme.Literate))
                .CreateLogger();

            ILoggerFactory logFactory = LoggerFactory.Create(builder =>
            {
                builder.ClearProviders().AddSerilog();
            });

            builder.Services.AddSingleton<Microsoft.Extensions.Logging.ILogger>(logFactory.CreateLogger(nameof(Program)));
        });

        Log.Information(DateTimeOffset.Now.ToString());

        string exp = configuration["exp"]
            ?? Environment.GetEnvironmentVariable("exp")
            ?? ClientVersion.SoM.ToString().ToLower(System.Globalization.CultureInfo.InvariantCulture);

        Log.Information($"Expansion: {exp}");

        services.AddMatBlazor();
        services.AddRazorPages();
        services.AddServerSideBlazor();
        services.AddSingleton<CancellationTokenSource>();
        services.AddSingleton<DataConfig>(x => DataConfig.Load(exp));
        services.AddSingleton<WorldMapAreaDB>();

        services.Configure<NavmeshBakeOptions>(configuration.GetSection(NavmeshBakeOptions.Position));
        services.Configure<NavmeshQueryOptions>(configuration.GetSection(NavmeshQueryOptions.Position));
        services.Configure<SplineFollowerOptions>(configuration.GetSection(SplineFollowerOptions.Position));

        services.AddSingleton<PPatherService>();
        services.AddSingleton<FactionTemplateDB>();
        services.AddSingleton<CreatureDB>();
        services.AddSingleton<AreaDB>();

        services.AddSingleton(provider =>
            provider.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions);

        services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
            options.SerializerOptions.Converters.Add(new Vector3Converter());
            options.SerializerOptions.Converters.Add(new Vector2Converter());
            options.SerializerOptions.Converters.Add(new Vector4Converter());
        });

        // Pull in the Frontend authoring controllers (road / danger zone) so the
        // cost-zone edit loop works against this host, which needs no game client.
        services.AddControllers()
            .AddApplicationPart(typeof(Frontend.Controllers.RoadController).Assembly)
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                options.JsonSerializerOptions.Converters.Add(new Vector3Converter());
                options.JsonSerializerOptions.Converters.Add(new Vector2Converter());
                options.JsonSerializerOptions.Converters.Add(new Vector4Converter());
            });

        services.AddSignalR()
            .AddMessagePackProtocol(options =>
            {
                options.SerializerOptions.WithCompression(MessagePack.MessagePackCompression.Lz4BlockArray);
            });

        // Register the Swagger generator, defining 1 or more Swagger documents
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Pathing API", Version = "v1" });

            //// Set the comments path for the Swagger JSON and UI.
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlDocumentPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlDocumentPath))
            {
                c.IncludeXmlComments(xmlDocumentPath);
            }
        });

        services.BuildServiceProvider(new ServiceProviderOptions() { ValidateOnBuild = true });
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        // "Pathing:Engine" selects the in-process engine (SpotAStar | Navmesh).
        if (System.Enum.TryParse(configuration.GetSection(StartupConfigPathing.Position)["Engine"],
            out PathingEngine engine))
        {
            app.ApplicationServices.GetRequiredService<PPatherService>().Engine = engine;
        }

        // Optional startup bake: `--bake=<continent>` kicks a background bake as
        // the server comes up, using the `--exp` expansion. `all` (or `*`) bakes
        // every continent; the API serves immediately and progress is on
        // GET api/PPather/Bake/Status.
        string? bake = configuration["bake"] ?? Environment.GetEnvironmentVariable("bake");
        if (!string.IsNullOrWhiteSpace(bake))
        {
            string? continent = bake is "all" or "*" ? null : bake;
            PPatherService service = app.ApplicationServices.GetRequiredService<PPatherService>();

            if (service.StartBake(continent, null))
            {
                Log.Information("Startup bake requested: {Scope} (expansion {Exp})",
                    continent ?? "all continents",
                    configuration["exp"] ?? Environment.GetEnvironmentVariable("exp") ?? "som");
            }
            else
            {
                Log.Warning("Startup bake could not start - a bake is already running.");
            }
        }

        // Optional one-time area-grid extraction: `--bake-area=<continent>` reads
        // the client ADTs and writes the standalone spatial area-id grid(s) under
        // DataConfig.AreaGrid, so GetAreaIdAndZ answers without the game files.
        // `all` (or `*`) does every continent.
        string? bakeArea = configuration["bake-area"] ?? Environment.GetEnvironmentVariable("bake-area");
        if (!string.IsNullOrWhiteSpace(bakeArea))
        {
            string? continent = bakeArea is "all" or "*" ? null : bakeArea;
            PPatherService service = app.ApplicationServices.GetRequiredService<PPatherService>();

            Log.Information("Startup area-grid bake requested: {Scope}", continent ?? "all continents");
            Task.Run(() => service.BuildAreaGrid(continent));
        }

        // Enable middleware to serve generated Swagger as a JSON endpoint.
        app.UseSwagger();

        // Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.),
        // specifying the Swagger JSON endpoint.
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "PPather API V1");
        });

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();

        app.UseCustomStaticFiles(env);

        app.UseRouting();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapHub<WatchHub>(WatchHub.Url);
            endpoints.MapBlazorHub();
            endpoints.MapFallbackToPage("/_Host");
            endpoints.MapControllers();
            endpoints.MapRazorPages();
        });
    }
}
