using Microsoft.AspNetCore.SignalR;

namespace PathingAPI;

public sealed class WatchHub : Hub
{
    public const string Url = "/watchHub";
}
