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

    /// <summary>
    /// Serves a data folder, creating it when absent. <see cref="PhysicalFileProvider"/>
    /// throws <see cref="DirectoryNotFoundException"/> on a missing root, which
    /// takes down host startup rather than merely 404ing that route - and these
    /// roots are era- and expansion-derived, so a client whose assets have not
    /// been generated yet (a new era, or a fresh install with no leaflet tiles)
    /// legitimately has none of them.
    /// </summary>
    private static void UseDataFolder(IApplicationBuilder app, string root, string requestPath)
    {
        Directory.CreateDirectory(root);

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(root),
            RequestPath = requestPath
        });
    }

    public static IApplicationBuilder UseCustomStaticFiles(this IApplicationBuilder app, IWebHostEnvironment env)
    {
        DataConfig dataConfig = app.ApplicationServices.GetRequiredService<DataConfig>();

        UseDataFolder(app, Path.Combine(env.ContentRootPath, dataConfig.Path), "/path");

        // Continents whose art this era shares with an earlier one are served
        // from that era's folder (see DataConfig.TileEra), so Northrend and
        // Outland are not duplicated for Cataclysm. Registered BEFORE the
        // general /tiles route below: static file middleware runs in order, so
        // the more specific RequestPath has to be first to win.
        foreach (string continent in DataConfig.TileSharedContinents)
        {
            if (DataConfig.TileEra(dataConfig.Exp, continent) == DataConfig.ClientEra(dataConfig.Exp))
            {
                continue;
            }

            string shared = Path.Combine(env.ContentRootPath, dataConfig.LeafletFor(continent));
            if (!Directory.Exists(shared))
            {
                continue;
            }

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(shared),
                RequestPath = "/tiles/" + continent
            });
        }

        UseDataFolder(app, Path.Combine(env.ContentRootPath, dataConfig.Leaflet), "/tiles");
        UseDataFolder(app, Path.Combine(env.ContentRootPath, dataConfig.ExpDbc), "/dbc");
        UseDataFolder(app, Path.Combine(env.ContentRootPath, dataConfig.ExpArea), "/area");
        UseDataFolder(app, Path.Combine(env.ContentRootPath, dataConfig.NpcSpawnLocations), "/npcspawnlocations");
        UseDataFolder(app, Path.Combine(env.ContentRootPath, dataConfig.MailboxLocations), "/mailboxlocations");

        return app;
    }
}
