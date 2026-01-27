using Makaretu.Dns;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BlazorServer;

/// <summary>
/// Background service that advertises the Blazor Server via mDNS,
/// making it accessible at http://wowbot.local
/// </summary>
public sealed class MdnsAdvertisingService : IHostedService, IDisposable
{
    private readonly ILogger<MdnsAdvertisingService> _logger;
    private readonly int _port;
    private readonly string _hostname;

    private MulticastService? _multicastService;
    private ServiceDiscovery? _serviceDiscovery;
    private ServiceProfile? _serviceProfile;

    public MdnsAdvertisingService(ILogger<MdnsAdvertisingService> logger)
    {
        _logger = logger;
        _hostname = "wowbot";
        _port = GetServerPort();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _multicastService = new MulticastService();
            _serviceDiscovery = new ServiceDiscovery(_multicastService);

            // Log discovered network interfaces
            _multicastService.NetworkInterfaceDiscovered += (s, e) =>
            {
                foreach (var nic in e.NetworkInterfaces)
                {
                    _logger.LogDebug("[mDNS             ] Discovered NIC '{NicName}'", nic.Name);
                }
            };

            // Log available IP addresses
            foreach (var ip in MulticastService.GetIPAddresses())
            {
                _logger.LogDebug("[mDNS             ] Available IP: {IpAddress}", ip);
            }

            // Create service profile - this automatically handles hostname and IP resolution
            _serviceProfile = new ServiceProfile(
                instanceName: _hostname,
                serviceName: "_http._tcp",
                port: (ushort)_port);

            // Add TXT records with service info
            _serviceProfile.AddProperty("path", "/");
            _serviceProfile.AddProperty("server", "BlazorServer");

            _multicastService.Start();

            // Probe to check if name is available, then advertise and announce
            if (!_serviceDiscovery.Probe(_serviceProfile))
            {
                _serviceDiscovery.Advertise(_serviceProfile);
                _serviceDiscovery.Announce(_serviceProfile);

                _logger.LogInformation(
                    "[mDNS             ] Advertising service at http://{Hostname}.local:{Port}",
                    _hostname, _port);
            }
            else
            {
                _logger.LogWarning(
                    "[mDNS             ] Service name '{Hostname}' already in use on network",
                    _hostname);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[mDNS             ] Failed to start mDNS advertising");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[mDNS             ] Stopping mDNS advertising");

        try
        {
            if (_serviceProfile != null)
            {
                _serviceDiscovery?.Unadvertise(_serviceProfile);
            }
            _serviceDiscovery?.Dispose();
            _multicastService?.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[mDNS             ] Error during mDNS shutdown");
        }

        return Task.CompletedTask;
    }

    private static int GetServerPort()
    {
        // Default Kestrel port; can be overridden via configuration
        var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        if (!string.IsNullOrEmpty(urls))
        {
            foreach (var url in urls.Split(';'))
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    return uri.Port;
                }
            }
        }

        return 5000; // Default port
    }

    public void Dispose()
    {
        _serviceDiscovery?.Dispose();
        _multicastService?.Dispose();
    }
}
