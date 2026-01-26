using AnTCP.Client.Objects;

using System;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace AnTCP.Client;

public sealed unsafe class AnTcpClient
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public AnTcpClient(string ip, int port)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    {
        Ip = ip;
        Port = port;
    }

    public string Ip { get; }

    public bool IsConnected => Client != null && Client.Connected;

    public int Port { get; }

    private TcpClient Client { get; set; }

    private BinaryReader Reader { get; set; }

    private NetworkStream Stream { get; set; }

    /// <summary>
    /// Connect to the server.
    /// </summary>
    public void Connect()
    {
        Client = new TcpClient(Ip, Port);
        Stream = Client.GetStream();
        Reader = new BinaryReader(Stream);
    }

    /// <summary>
    /// Disconnect from the server.
    /// </summary>
    public void Disconnect()
    {
        Stream.Close();
        Client.Close();
    }

    /// <summary>
    /// Send data to the server, data can be any unmanaged type.
    /// </summary>
    /// <typeparam name="T">Unamnaged type of the</typeparam>
    /// <param name="type">Mesagte type</param>
    /// <param name="data">Data to send</param>
    /// <returns>Server response</returns>
    public AnTcpResponse Send<T>(byte type, T data) where T : unmanaged
    {
        int size = sizeof(T);
        return SendData(BitConverter.GetBytes(size + 1).AsSpan(), new Span<byte>(&type, 1), new Span<byte>(&data, size));
    }

    /// <summary>
    /// Send a byte array to the server.
    /// </summary>
    /// <param name="type">Mesagte type</param>
    /// <param name="data">Data to send</param>
    /// <returns>Server response</returns>
    public AnTcpResponse SendBytes(byte type, byte[] data)
    {
        return SendData(BitConverter.GetBytes(data.Length + 1).AsSpan(), new Span<byte>(&type, 1), data.AsSpan());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private AnTcpResponse SendData(Span<byte> size, Span<byte> type, Span<byte> data)
    {
        Stream.Write(size);
        Stream.Write(type);
        Stream.Write(data);

        byte[] response = Reader.ReadBytes(Reader.ReadInt32());
        return new AnTcpResponse(response[0], response[1..response.Length]);
    }
}