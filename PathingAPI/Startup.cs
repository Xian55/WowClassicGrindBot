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

using SharedLib;
using SharedLib.Converters;

using System;
using System.IO;
using System.Reflection;
using System.Threading;

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

            const string outputTemplate = "[{Timestamp:HH:mm:ss:fff} {Level:u1}] {Message:lj}{NewLine}{Exception}";

            Log.Logger = new LoggerConfiguration()
                //.MinimumLevel.Debug()
                //.MinimumLevel.Verbose()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .WriteTo.Sink(sink)
                .WriteTo.File("out.log",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: outputTemplate)
                .WriteTo.Debug(outputTemplate: outputTemplate)
                .WriteTo.Console(outputTemplate: outputTemplate)
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
            options.SerializerOptions.Converters.Add(new Vector4Converter());
        });

        services.AddControllers().AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
            options.JsonSerializerOptions.Converters.Add(new Vector3Converter());
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
