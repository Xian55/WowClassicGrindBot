using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;

namespace PathingAPI;

public sealed class Program
{
    private const string DefaultHostUrl = "http://127.0.0.1:5001";
    private const string HostUrlEnvVar = "HOST_URL";
    private const string AspNetCoreUrlsEnvVar = "ASPNETCORE_URLS";

    public static void Main(string[] args)
    {
        string hostUrl = ResolveHostUrl();

        CreateHostBuilder(args, hostUrl).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args, string hostUrl) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddCommandLine(args);
                    config.AddEnvironmentVariables(prefix: "PATHINGAPI_");
                });

                webBuilder.UseUrls(hostUrl);
                webBuilder.ConfigureLogging(logging =>
                    logging.ClearProviders().AddSerilog());
                webBuilder.UseStartup<Startup>();
            });

    private static string ResolveHostUrl()
    {
        string hostUrl = Environment.GetEnvironmentVariable(HostUrlEnvVar);
        if (!string.IsNullOrWhiteSpace(hostUrl))
        {
            return hostUrl;
        }

        string aspNetCoreUrls = Environment.GetEnvironmentVariable(AspNetCoreUrlsEnvVar);
        if (!string.IsNullOrWhiteSpace(aspNetCoreUrls))
        {
            return aspNetCoreUrls;
        }

        return DefaultHostUrl;
    }
}
