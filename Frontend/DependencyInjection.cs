using System.IO;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Frontend;

public static class DependencyInjection
{
    public static IServiceCollection AddFrontend(this IServiceCollection services)
    {
        services.AddBlazorBootstrap();

        services.AddRazorPages();

        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        return services;
    }

    public static IApplicationBuilder UseCustomStaticFiles(this IApplicationBuilder app, IWebHostEnvironment env)
    {
        DataConfig dataConfig = app.ApplicationServices.GetRequiredService<DataConfig>();

        ServeIfExists(app, Path.Combine(env.ContentRootPath, dataConfig.Path), "/path");
        ServeIfExists(app, Path.Combine(env.ContentRootPath, dataConfig.Leaflet), "/tiles");
        ServeIfExists(app, Path.Combine(env.ContentRootPath, dataConfig.ExpDbc), "/dbc");
        ServeIfExists(app, Path.Combine(env.ContentRootPath, dataConfig.ExpArea), "/area");
        ServeIfExists(app, Path.Combine(env.ContentRootPath, dataConfig.NpcSpawnLocations), "/npcspawnlocations");
        ServeIfExists(app, Path.Combine(env.ContentRootPath, dataConfig.MailboxLocations), "/mailboxlocations");

        return app;
    }

    private static void ServeIfExists(IApplicationBuilder app, string path, string requestPath)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(path),
            RequestPath = requestPath
        });
    }
}
