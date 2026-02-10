using System;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using PathingAPI;

using Serilog;

namespace PathingAPI.Container;

public static class Program
{
    private const string DefaultHostUrl = "http://0.0.0.0:5001";
    private const string HostUrlEnvVar = "HOST_URL";
    private const string AspNetCoreUrlsEnvVar = "ASPNETCORE_URLS";

    public static void Main(string[] args)
    {
        string hostUrl = ResolveHostUrl();

        CreateHostBuilder(args, hostUrl)
            .Build()
            .Run();
    }

    private static IHostBuilder CreateHostBuilder(string[] args, string hostUrl) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddCommandLine(args);
                config.AddEnvironmentVariables(prefix: "PATHINGAPI_");
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseUrls(hostUrl);
                webBuilder.ConfigureLogging(logging =>
                    logging.ClearProviders().AddSerilog());
                webBuilder.UseStartup<Startup>();
            });

    private static string ResolveHostUrl()
    {
        string? hostUrl = Environment.GetEnvironmentVariable(HostUrlEnvVar);
        if (!string.IsNullOrWhiteSpace(hostUrl))
        {
            return hostUrl;
        }

        string? aspNetCoreUrls = Environment.GetEnvironmentVariable(AspNetCoreUrlsEnvVar);
        if (!string.IsNullOrWhiteSpace(aspNetCoreUrls))
        {
            return aspNetCoreUrls;
        }

        return DefaultHostUrl;
    }
}
