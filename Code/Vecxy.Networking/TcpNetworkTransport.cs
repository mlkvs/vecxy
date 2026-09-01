using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Vecxy.Networking;

public sealed class TcpNetworkTransport : INetworkTransport
{
    private readonly ConcurrentDictionary<NetworkConnection, Peer> _peers = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TcpListener? _listener;
    private long _nextConnectionId;
    private bool _disposed;

    private TcpNetworkTransport(TcpListener? listener) => _listener = listener;

    public event Action<NetworkReceive>? Received;
    public event Action<NetworkConnection>? Connected;
    public event Action<NetworkConnection>? Disconnected;
    public IReadOnlyCollection<NetworkConnection> Connections => _peers.Keys.ToArray();
    public NetworkConnection? LocalConnection { get; private set; }
    public NetworkConnection? ServerConnection { get; private set; }

    public static Task<TcpNetworkTransport> StartServerAsync(int port, CancellationToken cancellationToken = default)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        var transport = new TcpNetworkTransport(listener);
        _ = transport.AcceptLoopAsync();
        return Task.FromResult(transport);
    }

    public static async Task<TcpNetworkTransport> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(host, port, cancellationToken);
        var stream = client.GetStream();
        var idBytes = new byte[8];
        await ReadExactlyAsync(stream, idBytes, cancellationToken);
        var transport = new TcpNetworkTransport(null);
        transport.LocalConnection = new NetworkConnection(BinaryPrimitives.ReadUInt64LittleEndian(idBytes));
        transport.ServerConnection = new NetworkConnection(0);
        transport.AddPeer(transport.ServerConnection, client);
        transport.Connected?.Invoke(transport.ServerConnection);
        return transport;
    }

    public void Send(NetworkConnection connection, ReadOnlySpan<byte> data, RpcChannel channel)
    {
        if (!_peers.TryGetValue(connection, out var peer) || !connection.IsConnected)
            throw new InvalidOperationException($"Connection {connection} is not active.");
        var frame = new byte[checked(5 + data.Length)];
        BinaryPrimitives.WriteInt32LittleEndian(frame, data.Length);
        frame[4] = (byte)channel;
        data.CopyTo(frame.AsSpan(5));
        lock (peer.SendLock) peer.Stream.Write(frame);
    }

    public void Disconnect(NetworkConnection connection, string reason)
    {
        if (!_peers.TryRemove(connection, out var peer)) return;
        connection.IsConnected = false;
        peer.Client.Dispose();
        Disconnected?.Invoke(connection);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(_shutdown.Token);
                client.NoDelay = true;
                var connection = new NetworkConnection(checked((ulong)Interlocked.Increment(ref _nextConnectionId)));
                var id = new byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(id, connection.Id);
                await client.GetStream().WriteAsync(id, _shutdown.Token);
                AddPeer(connection, client);
                Connected?.Invoke(connection);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested) { break; }
        }
    }

    private void AddPeer(NetworkConnection connection, TcpClient client)
    {
        var peer = new Peer(client);
        if (!_peers.TryAdd(connection, peer)) throw new InvalidOperationException("Duplicate TCP connection.");
        _ = ReceiveLoopAsync(connection, peer);
    }

    private async Task ReceiveLoopAsync(NetworkConnection connection, Peer peer)
    {
        var header = new byte[5];
        try
        {
            while (!_shutdown.IsCancellationRequested && connection.IsConnected)
            {
                await ReadExactlyAsync(peer.Stream, header, _shutdown.Token);
                var length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length is < 0 or > 16 * 1024 * 1024) throw new InvalidDataException("Invalid TCP network frame length.");
                var payload = new byte[length];
                await ReadExactlyAsync(peer.Stream, payload, _shutdown.Token);
                Received?.Invoke(new NetworkReceive(connection, payload, (RpcChannel)header[4]));
            }
        }
        catch (Exception exception) when (exception is IOException or SocketException or EndOfStreamException or OperationCanceledException) { }
        finally { Disconnect(connection, "Connection closed."); }
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[read..], cancellationToken);
            if (count == 0) throw new EndOfStreamException();
            read += count;
        }
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _shutdown.Cancel(); _listener?.Stop();
        foreach (var connection in _peers.Keys.ToArray()) Disconnect(connection, "Transport stopped.");
        _shutdown.Dispose();
    }

    private sealed class Peer(TcpClient client)
    {
        public TcpClient Client { get; } = client;
        public NetworkStream Stream { get; } = client.GetStream();
        public Lock SendLock { get; } = new();
    }
}
