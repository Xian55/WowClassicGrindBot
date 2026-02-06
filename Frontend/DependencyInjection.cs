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
        string root = env.ContentRootPath;

        AddStaticFiles(app, root, dataConfig.Path, "/path");
        AddStaticFiles(app, root, dataConfig.Leaflet, "/tiles");
        AddStaticFiles(app, root, dataConfig.ExpDbc, "/dbc");
        AddStaticFiles(app, root, dataConfig.ExpArea, "/area");
        AddStaticFiles(app, root, dataConfig.NpcSpawnLocations, "/npcspawnlocations");
        AddStaticFiles(app, root, dataConfig.MailboxLocations, "/mailboxlocations");

        return app;
    }

    private static void AddStaticFiles(IApplicationBuilder app, string root, string subPath, string requestPath)
    {
        string fullPath = Path.Combine(root, subPath);
        Directory.CreateDirectory(fullPath);

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(fullPath),
            RequestPath = requestPath
        });
    }
}
