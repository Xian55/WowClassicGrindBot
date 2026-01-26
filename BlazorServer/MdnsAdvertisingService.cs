using Makaretu.Dns;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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

            // Advertise the hostname (wowbot.local)
            AdvertiseHostname();

            // Advertise the HTTP service
            AdvertiseHttpService();

            _multicastService.Start();

            _logger.LogInformation(
                "[mDNS             ] Advertising service at http://{Hostname}.local:{Port}",
                _hostname, _port);
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
            _serviceDiscovery?.Unadvertise(_serviceProfile);
            _serviceDiscovery?.Dispose();
            _multicastService?.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[mDNS             ] Error during mDNS shutdown");
        }

        return Task.CompletedTask;
    }

    private void AdvertiseHostname()
    {
        if (_multicastService == null) return;

        // Register A records for each network interface IP
        _multicastService.QueryReceived += (s, e) =>
        {
            var query = e.Message;
            foreach (var question in query.Questions)
            {
                if (question.Name.ToString().Equals($"{_hostname}.local", StringComparison.OrdinalIgnoreCase))
                {
                    var response = query.CreateResponse();

                    foreach (var ip in GetLocalIPAddresses())
                    {
                        if (ip.AddressFamily == AddressFamily.InterNetwork)
                        {
                            response.Answers.Add(new ARecord
                            {
                                Name = $"{_hostname}.local",
                                Address = ip,
                                TTL = TimeSpan.FromMinutes(2)
                            });
                        }
                        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                        {
                            response.Answers.Add(new AAAARecord
                            {
                                Name = $"{_hostname}.local",
                                Address = ip,
                                TTL = TimeSpan.FromMinutes(2)
                            });
                        }
                    }

                    if (response.Answers.Count > 0)
                    {
                        _multicastService.SendAnswer(response);
                    }
                }
            }
        };
    }

    private void AdvertiseHttpService()
    {
        if (_multicastService == null) return;

        _serviceDiscovery = new ServiceDiscovery(_multicastService);

        // Create service profile for HTTP
        _serviceProfile = new ServiceProfile(
            instanceName: _hostname,
            serviceName: "_http._tcp",
            port: (ushort)_port);

        _serviceProfile.HostName = $"{_hostname}.local";

        // Add TXT records with service info
        _serviceProfile.AddProperty("path", "/");
        _serviceProfile.AddProperty("server", "BlazorServer");

        _serviceDiscovery.Advertise(_serviceProfile);
    }

    private static IPAddress[] GetLocalIPAddresses()
    {
        var addresses = new List<IPAddress>();

        foreach (var netInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (netInterface.OperationalStatus != OperationalStatus.Up)
                continue;

            if (netInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            var ipProps = netInterface.GetIPProperties();
            foreach (var addr in ipProps.UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork ||
                    addr.Address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    // Skip link-local IPv6 addresses
                    if (addr.Address.IsIPv6LinkLocal)
                        continue;

                    addresses.Add(addr.Address);
                }
            }
        }

        return addresses.ToArray();
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
