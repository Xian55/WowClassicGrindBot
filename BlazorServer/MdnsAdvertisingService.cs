using Makaretu.Dns;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BlazorServer;

/// <summary>
/// Background service that advertises the Blazor Server via mDNS,
/// making it accessible at http://wowbot.local or whatever is set at MDNS_HOSTNAME
/// </summary>
public sealed class MdnsAdvertisingService : IHostedService, IDisposable
{
    private readonly ILogger<MdnsAdvertisingService> _logger;
    private int _port;
    private readonly string _hostname;
    private readonly IServer _server;
    private readonly IHostApplicationLifetime _lifetime;
    private CancellationTokenRegistration _startedRegistration;
    private MulticastService? _multicastService;
    private ServiceDiscovery? _serviceDiscovery;
    private ServiceProfile? _serviceProfile;
    private bool _isAdvertising;

    // Cached IP addresses - refreshed when network interfaces change
    private IPAddress[] _cachedAddresses = [];
    private readonly Lock _addressLock = new();

    public MdnsAdvertisingService(
        ILogger<MdnsAdvertisingService> logger, 
        IServer server,
        IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        _server = server;
        _lifetime = lifetime;
        _hostname = Environment.GetEnvironmentVariable("MDNS_HOSTNAME") ?? "wowbot";
        _port = 5000;

        // Subscribe to network address changes
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        _logger.LogDebug("mDNS: Network address changed, refreshing IP cache");
        RefreshIPAddressCache();

        // Re-announce with new addresses
        if (_isAdvertising)
        {
            AnnounceHostname();
        }
    }

    private void RefreshIPAddressCache()
    {
        var addresses = new System.Collections.Generic.List<IPAddress>();

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
                    addresses.Add(addr.Address);
                }
            }
        }

        using (_addressLock.EnterScope())
        {
            _cachedAddresses = addresses.ToArray();
        }
    }

    private IPAddress[] GetCachedAddresses()
    {
        using (_addressLock.EnterScope())
        {
            return _cachedAddresses;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Initialize IP address cache
            RefreshIPAddressCache();
            
            // Using Application started event to get the port from Kestral
            _startedRegistration = _lifetime.ApplicationStarted.Register(() =>
            {
                var addressFeature = _server.Features.Get<IServerAddressesFeature>();
                if (addressFeature != null)
                {
                    foreach (var address in addressFeature.Addresses)
                    {
                        if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
                        {
                            _port = uri.Port;
                            _logger.LogDebug("mDNS: Detected server port {Port} from {Address}", _port, address);
                            break;
                        }
                    }
                }

                _multicastService = new MulticastService();
                _serviceDiscovery = new ServiceDiscovery(_multicastService);

                // Respond to direct hostname queries (e.g., ping wowbot.local)
                _multicastService.QueryReceived += OnQueryReceived;

                // Create service profile
                _serviceProfile = new ServiceProfile(
                    instanceName: _hostname,
                    serviceName: "_http._tcp",
                    port: (ushort)_port);

                // Add TXT records
                _serviceProfile.AddProperty("path", "/");
                _serviceProfile.AddProperty("server", "BlazorServer");

                // Start the multicast service
                _multicastService.Start();
                _logger.LogInformation("mDNS: Multicast service started");

                // Advertise and announce the service
                _serviceDiscovery.Advertise(_serviceProfile);
                _serviceDiscovery.Announce(_serviceProfile);
                _isAdvertising = true;

                // Also announce our hostname A record
                AnnounceHostname();

                if(_logger.IsEnabled(LogLevel.Information)) 
                {
                    _logger.LogInformation(
                        "mDNS: Service advertised at http://{Hostname}.local:{Port}",
                        _hostname, _port);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "mDNS: Failed to start advertising");
        }

        return Task.CompletedTask;
    }

    private void OnQueryReceived(object? sender, MessageEventArgs e)
    {
        var domainName = $"{_hostname}.local";

        foreach (var question in e.Message.Questions)
        {
            // Check if someone is asking for our hostname
            if (question.Name.ToString().Equals(domainName, StringComparison.OrdinalIgnoreCase))
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("mDNS: Received query for {Name} (Type: {Type})", question.Name, question.Type);
                }

                var response = e.Message.CreateResponse();
                var addresses = GetCachedAddresses();

                foreach (var ip in addresses)
                {
                    if (question.Type == DnsType.A && ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        response.Answers.Add(new ARecord
                        {
                            Name = domainName,
                            Address = ip,
                            TTL = TimeSpan.FromMinutes(2)
                        });
                        if (_logger.IsEnabled(LogLevel.Debug))
                        {
                            _logger.LogDebug("mDNS: Responding with A record: {Ip}", ip);
                        }
                    }
                    else if (question.Type == DnsType.AAAA && ip.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        response.Answers.Add(new AAAARecord
                        {
                            Name = domainName,
                            Address = ip,
                            TTL = TimeSpan.FromMinutes(2)
                        });
                        if (_logger.IsEnabled(LogLevel.Debug))
                        {
                            _logger.LogDebug("mDNS: Responding with AAAA record: {Ip}", ip);
                        }
                    }
                    else if (question.Type == DnsType.ANY)
                    {
                        if (ip.AddressFamily == AddressFamily.InterNetwork)
                        {
                            response.Answers.Add(new ARecord
                            {
                                Name = domainName,
                                Address = ip,
                                TTL = TimeSpan.FromMinutes(2)
                            });
                        }
                        else if (ip.AddressFamily == AddressFamily.InterNetworkV6 && !ip.IsIPv6LinkLocal)
                        {
                            response.Answers.Add(new AAAARecord
                            {
                                Name = domainName,
                                Address = ip,
                                TTL = TimeSpan.FromMinutes(2)
                            });
                        }
                    }
                }

                if (response.Answers.Count > 0)
                {
                    _multicastService?.SendAnswer(response);
                }
            }
        }
    }

    private void AnnounceHostname()
    {
        if (_multicastService == null) return;

        var domainName = $"{_hostname}.local";
        var response = new Message();
        response.QR = true; // This is a response
        response.AA = true; // Authoritative answer

        foreach (var ip in GetCachedAddresses())
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                response.Answers.Add(new ARecord
                {
                    Name = domainName,
                    Address = ip,
                    TTL = TimeSpan.FromMinutes(2)
                });
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("mDNS: Announcing A record: {Hostname} -> {Ip}", domainName, ip);
                }
            }
            else if (ip.AddressFamily == AddressFamily.InterNetworkV6 && !ip.IsIPv6LinkLocal)
            {
                response.Answers.Add(new AAAARecord
                {
                    Name = domainName,
                    Address = ip,
                    TTL = TimeSpan.FromMinutes(2)
                });
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("mDNS: Announcing AAAA record: {Hostname} -> {Ip}", domainName, ip);
                }
            }
        }

        if (response.Answers.Count > 0)
        {
            _multicastService.SendAnswer(response);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("mDNS: Stopping advertising");

        // Unsubscribe from network changes
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;

        try
        {
            if (_isAdvertising && _serviceProfile != null && _serviceDiscovery != null)
            {
                _serviceDiscovery.Unadvertise(_serviceProfile);
                _isAdvertising = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "mDNS: Error during unadvertise");
        }

        try
        {
            _multicastService?.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "mDNS: Error stopping multicast service");
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _serviceDiscovery?.Dispose();
        _multicastService?.Dispose();
    }
}