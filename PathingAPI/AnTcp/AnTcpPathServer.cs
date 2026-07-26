using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using PPather;
using PPather.Graph;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace PathingAPI.AnTcp;

/// <summary>
/// Speaks AmeisenNavigation's AnTCP protocol so <c>RemotePathingAPIV3</c> can talk to
/// this server instead of the external AmeisenNavigation binary - same wire format,
/// but answered from this project's era-aware navmesh, so no MMAPs and no second
/// process. Enable with <c>Pathing:AnTcp:Enabled=true</c>.
///
/// Only <see cref="MessageType.Path"/> is implemented, because it is the only message
/// the bot ever sends: RemotePathingAPIV3 hardcodes <c>TYPE = EMessageType.PATH</c>
/// and its sole other traffic is the TCP connect used as a ping. Any other type gets
/// an explicit empty reply rather than silence, so a client that does send one fails
/// fast instead of blocking on a read.
///
/// Wire format (little-endian, matching the C++ structs byte for byte):
///   request   int32 size(payload+1) | byte type | int32 mapId | int32 flags | float start[3] | float end[3]
///   response  int32 size(payload+1) | byte type | Vector3[n]
/// A single all-zero Vector3 means "no path", which is what AmeisenNavigation returns
/// on failure and what the client already tests for.
/// </summary>
public sealed class AnTcpPathServer : BackgroundService
{
    /// <summary>Request payload: int mapId, int flags, Vector3 start, Vector3 end.</summary>
    private const int PathRequestSize = 4 + 4 + 12 + 12;

    /// <summary>Bytes of a System.Numerics.Vector3 on the wire.</summary>
    private const int Vector3Size = 12;

    /// <summary>
    /// Frame guard. The only request is 33 bytes; anything larger is a desynced
    /// stream or a wrong-protocol client, and reading it would allocate blindly.
    /// </summary>
    private const int MaxFrameSize = 4096;

    private enum MessageType : byte
    {
        Path = 0,
        MoveAlongSurface = 1,
        RandomPoint = 2,
        RandomPointAround = 3,
        CastRay = 4,
        RandomPath = 5,
        ExplorePoly = 6,
        ConfigureFilter = 7,
    }

    private const SearchStrategy Search = SearchStrategy.A_Star_With_Model_Avoidance;

    private readonly ILogger<AnTcpPathServer> logger;
    private readonly PPatherService service;
    private readonly IPEndPoint endpoint;

    // PPatherService is a singleton whose search is a SetLocations-then-DoSearch
    // sequence over shared state, so two clients interleaving would return each
    // other's routes. Serialize instead of rejecting: a warm navmesh query is
    // single-digit milliseconds, so waiting beats the HTTP API's 429 (which the
    // V1 client cannot distinguish from "no path exists").
    private readonly SemaphoreSlim searchLock = new(1, 1);

    public AnTcpPathServer(ILogger<AnTcpPathServer> logger, PPatherService service,
        string ip, int port)
    {
        this.logger = logger;
        this.service = service;
        endpoint = new IPEndPoint(IPAddress.Parse(ip), port);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TcpListener listener = new(endpoint);
        listener.Start();

        logger.LogInformation("AnTCP path server listening on {Endpoint} (PATH only)", endpoint);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(stoppingToken);
                _ = Task.Run(() => ServeAsync(client, stoppingToken), CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        finally
        {
            listener.Stop();
            logger.LogInformation("AnTCP path server stopped");
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken token)
    {
        EndPoint? remote = client.Client.RemoteEndPoint;
        logger.LogInformation("AnTCP client connected: {Remote}", remote);

        try
        {
            client.NoDelay = true;   // request/response, never batched - latency over throughput
            using NetworkStream stream = client.GetStream();

            byte[] header = new byte[sizeof(int)];

            while (!token.IsCancellationRequested)
            {
                // ReadExactly, not Read: a 4-byte header can legitimately arrive split.
                try
                {
                    await stream.ReadExactlyAsync(header, token);
                }
                catch (EndOfStreamException)
                {
                    break;   // client closed
                }

                int size = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (size < 1 || size > MaxFrameSize)
                {
                    logger.LogWarning("AnTCP {Remote}: bad frame size {Size}, dropping", remote, size);
                    break;
                }

                byte[] frame = new byte[size];
                await stream.ReadExactlyAsync(frame, token);

                byte type = frame[0];
                ReadOnlyMemory<byte> payload = frame.AsMemory(1);

                byte[] response = Handle(type, payload.Span, remote);
                await WriteAsync(stream, type, response, token);
            }
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException)
        {
            // Client vanished or shutdown - not worth a stack trace.
            logger.LogInformation("AnTCP client disconnected: {Remote}", remote);
        }
        catch (Exception e)
        {
            logger.LogError(e, "AnTCP client {Remote} failed", remote);
        }
        finally
        {
            client.Dispose();
        }
    }

    private static async Task WriteAsync(NetworkStream stream, byte type, byte[] payload,
        CancellationToken token)
    {
        byte[] buffer = new byte[sizeof(int) + 1 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, payload.Length + 1);
        buffer[sizeof(int)] = type;
        payload.CopyTo(buffer, sizeof(int) + 1);

        await stream.WriteAsync(buffer, token);
    }

    private byte[] Handle(byte type, ReadOnlySpan<byte> payload, EndPoint? remote)
    {
        if (type != (byte)MessageType.Path)
        {
            logger.LogWarning(
                "AnTCP {Remote}: message type {Type} is not implemented (PATH only); replying empty",
                remote, Enum.IsDefined(typeof(MessageType), type) ? (MessageType)type : type);
            return [];
        }

        if (payload.Length < PathRequestSize)
        {
            logger.LogWarning("AnTCP {Remote}: PATH payload is {Actual} bytes, expected {Expected}",
                remote, payload.Length, PathRequestSize);
            return NoPath();
        }

        int mapId = BinaryPrimitives.ReadInt32LittleEndian(payload);
        // flags (SMOOTH_*/VALIDATE_*) are read for completeness but not acted on: the
        // navmesh engine always returns a Catmull-Rom smoothed, CPOP-validated path,
        // which is exactly the combination RemotePathingAPIV3 asks for.
        int flags = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);

        Vector3 start = ReadVector3(payload[8..]);
        Vector3 end = ReadVector3(payload[20..]);

        return FindPath(mapId, flags, start, end, remote);
    }

    private static Vector3 ReadVector3(ReadOnlySpan<byte> b) => new(
        BinaryPrimitives.ReadSingleLittleEndian(b),
        BinaryPrimitives.ReadSingleLittleEndian(b[4..]),
        BinaryPrimitives.ReadSingleLittleEndian(b[8..]));

    /// <summary>
    /// How far a requested z may sit from the navmesh surface before it is treated as
    /// "the caller does not know its height" rather than a real floor.
    ///
    /// This exists because of what the client actually sends: when
    /// <c>RemotePathingAPIV3</c> has no z it substitutes <c>area.LocTop / 2</c> - and
    /// LocTop is a world *Y* bound, not a height, so for Elwynn it sends -3969 against
    /// terrain at 82. AmeisenNavigation tolerates that through a very tall
    /// findNearestPoly extent; our endpoint resolver uses tight vertical extents on
    /// purpose (it is what keeps multi-floor buildings honest), so the search would
    /// simply find nothing. Measured: -3969 returns NO PATH, 0 and 82 both return 410
    /// points.
    ///
    /// The band is wide enough that a bot genuinely standing on an upper floor keeps
    /// its own z and still disambiguates by height.
    /// </summary>
    private const float MaxPlausibleZDeltaYd = 128f;

    /// <summary>
    /// Replaces a height that lies nowhere near the mesh with the walkable surface at
    /// that column. Uses the disk-only per-continent query navmesh, so it neither
    /// switches the active continent nor needs game files.
    /// </summary>
    private Vector3 SnapImplausibleZ(int mapId, Vector3 p, string which, EndPoint? remote)
    {
        (_, float surface) = service.GetAreaIdAndZ(mapId, p.X, p.Y);

        // 0 means "no navmesh covers this column" - there is nothing better to offer.
        if (surface == 0f || MathF.Abs(p.Z - surface) <= MaxPlausibleZDeltaYd)
        {
            return p;
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AnTCP {Remote}: {Which} z {Given} is {Delta:F0}yd off the mesh, using {Surface}",
                remote, which, p.Z, MathF.Abs(p.Z - surface), surface);
        }

        return new(p.X, p.Y, surface);
    }

    private byte[] FindPath(int mapId, int flags, Vector3 start, Vector3 end, EndPoint? remote)
    {
        searchLock.Wait();
        try
        {
            start = SnapImplausibleZ(mapId, start, "start", remote);
            end = SnapImplausibleZ(mapId, end, "end", remote);

            service.SetLocations(new(start, mapId), new(end, mapId));
            // Fully qualified: System.IO.Path is in scope for the stream code above.
            PPather.Graph.Path? path = service.DoSearch(Search);

            if (path == null || path.locations.Count == 0)
            {
                return NoPath();
            }

            List<Vector3> points = path.locations;
            byte[] result = new byte[points.Count * Vector3Size];
            Span<byte> span = result;

            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points[i];
                Span<byte> at = span[(i * Vector3Size)..];
                BinaryPrimitives.WriteSingleLittleEndian(at, p.X);
                BinaryPrimitives.WriteSingleLittleEndian(at[4..], p.Y);
                BinaryPrimitives.WriteSingleLittleEndian(at[8..], p.Z);
            }

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("AnTCP {Remote}: PATH map {MapId} flags {Flags} {Start} -> {End} = {Count} points",
                    remote, mapId, flags, start, end, points.Count);
            }

            return result;
        }
        catch (Exception e)
        {
            logger.LogError(e, "AnTCP {Remote}: PATH map {MapId} {Start} -> {End} failed",
                remote, mapId, start, end);
            return NoPath();
        }
        finally
        {
            searchLock.Release();
        }
    }

    /// <summary>One all-zero Vector3 - AmeisenNavigation's failure reply, which the client tests for.</summary>
    private static byte[] NoPath() => new byte[Vector3Size];

    public override void Dispose()
    {
        searchLock.Dispose();
        base.Dispose();
    }
}
