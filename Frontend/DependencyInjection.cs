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

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.Combine(env.ContentRootPath, dataConfig.Path)),
            RequestPath = "/path"
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.Combine(env.ContentRootPath, dataConfig.Leaflet)),
            RequestPath = "/tiles"
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.Combine(env.ContentRootPath, dataConfig.ExpDbc)),
            RequestPath = "/dbc"
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.Combine(env.ContentRootPath, dataConfig.ExpArea)),
            RequestPath = "/area"
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.Combine(env.ContentRootPath, dataConfig.NpcSpawnLocations)),
            RequestPath = "/npcspawnlocations"
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.Combine(env.ContentRootPath, dataConfig.MailboxLocations)),
            RequestPath = "/mailboxlocations"
        });

        return app;
    }
}
