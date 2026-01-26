using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;

namespace PathingAPI;

public sealed class Program
{
    public static string hostUrl = "http://127.0.0.1:5001";

    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddCommandLine(args);
                });

                webBuilder.UseUrls(hostUrl);
                webBuilder.ConfigureLogging(logging =>
                    logging.ClearProviders().AddSerilog());
                webBuilder.UseStartup<Startup>();
            });
}
