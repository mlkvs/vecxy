using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Vecxy.Networking;

/// <summary>A UDP game transport with a small acknowledged reliable channel.</summary>
public sealed class UdpNetworkTransport : INetworkTransport
{
    private const uint Magic = 0x59584356; // VCXY
    private const int HeaderSize = 18;
    private static readonly TimeSpan ResendDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan PeerTimeout = TimeSpan.FromSeconds(10);
    private readonly UdpClient _socket;
    private readonly bool _server;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<NetworkConnection, Peer> _peers = new();
    private readonly ConcurrentDictionary<string, Peer> _endpoints = new();
    private readonly TaskCompletionSource<bool> _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _nextConnectionId;
    private bool _disposed;

    private UdpNetworkTransport(UdpClient socket, bool server)
    {
        _socket = socket;
        _server = server;
        _ = ReceiveLoopAsync();
        _ = MaintenanceLoopAsync();
    }

    public event Action<NetworkReceive>? Received;
    public event Action<NetworkConnection>? Connected;
    public event Action<NetworkConnection>? Disconnected;
    public IReadOnlyCollection<NetworkConnection> Connections => _peers.Keys.ToArray();
    public NetworkConnection? LocalConnection { get; private set; }
    public NetworkConnection? ServerConnection { get; private set; }

    public static Task<UdpNetworkTransport> StartServerAsync(int port, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new UdpNetworkTransport(new UdpClient(new IPEndPoint(IPAddress.Any, port)), true));
    }

    public static async Task<UdpNetworkTransport> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        var address = addresses.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork) ?? addresses.First();
        var socket = new UdpClient(address.AddressFamily);
        socket.Connect(new IPEndPoint(address, port));
        var transport = new UdpNetworkTransport(socket, false);
        using var registration = cancellationToken.Register(() => transport._connected.TrySetCanceled(cancellationToken));
        try
        {
            while (!transport._connected.Task.IsCompleted)
            {
                await transport.SendRawAsync(PacketType.Connect, 0, 0, RpcChannel.Reliable, ReadOnlyMemory<byte>.Empty);
                await Task.WhenAny(transport._connected.Task, Task.Delay(250, cancellationToken));
            }
            await transport._connected.Task;
            return transport;
        }
        catch
        {
            transport.Dispose();
            throw;
        }
    }

    public void Send(NetworkConnection connection, ReadOnlySpan<byte> data, RpcChannel channel)
    {
        if (!_peers.TryGetValue(connection, out var peer) || !connection.IsConnected)
            throw new InvalidOperationException($"Connection {connection} is not active.");
        if (data.Length > 60 * 1024) throw new InvalidDataException("UDP payload is too large.");
        var sequence = channel == RpcChannel.Reliable ? peer.NextSequence() : 0u;
        var packet = CreatePacket(PacketType.Data, connection.Id, sequence, channel, data);
        if (channel == RpcChannel.Reliable) peer.Pending[sequence] = new PendingPacket(packet, DateTime.UtcNow);
        SendDatagram(packet, peer.Endpoint);
    }

    public void Disconnect(NetworkConnection connection, string reason)
    {
        if (!_peers.TryRemove(connection, out var peer)) return;
        _endpoints.TryRemove(Key(peer.Endpoint), out _);
        connection.IsConnected = false;
        var message = System.Text.Encoding.UTF8.GetBytes(reason);
        SendDatagram(CreatePacket(PacketType.Disconnect, connection.Id, 0, RpcChannel.Reliable, message), peer.Endpoint);
        Disconnected?.Invoke(connection);
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                var result = await _socket.ReceiveAsync(_shutdown.Token);
                HandleDatagram(result.Buffer, result.RemoteEndPoint);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested) { }
        catch (SocketException) when (_shutdown.IsCancellationRequested) { }
    }

    private void HandleDatagram(ReadOnlySpan<byte> packet, IPEndPoint endpoint)
    {
        if (packet.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(packet) != Magic) return;
        var type = (PacketType)packet[4];
        var channel = (RpcChannel)packet[5];
        var connectionId = BinaryPrimitives.ReadUInt64LittleEndian(packet[6..]);
        var sequence = BinaryPrimitives.ReadUInt32LittleEndian(packet[14..]);
        var payload = packet[HeaderSize..];

        if (type == PacketType.Connect && _server) { Accept(endpoint); return; }
        if (type == PacketType.ConnectAccepted && !_server) { CompleteConnect(connectionId, endpoint); return; }
        if (!_endpoints.TryGetValue(Key(endpoint), out var peer) || peer.Connection.Id != connectionId) return;
        peer.LastSeenUtc = DateTime.UtcNow;
        if (!peer.AllowPacket()) { Disconnect(peer.Connection, "Packet rate limit exceeded."); return; }

        switch (type)
        {
            case PacketType.Ack:
                peer.Pending.TryRemove(sequence, out _);
                break;
            case PacketType.Data:
                if (channel == RpcChannel.Reliable)
                {
                    SendDatagram(CreatePacket(PacketType.Ack, connectionId, sequence, channel, []), endpoint);
                    foreach (var orderedPayload in peer.Order(sequence, payload))
                        Received?.Invoke(new NetworkReceive(peer.Connection, orderedPayload, channel));
                    return;
                }
                Received?.Invoke(new NetworkReceive(peer.Connection, payload.ToArray(), channel));
                break;
            case PacketType.Disconnect:
                DisconnectLocal(peer);
                break;
        }
    }

    private void Accept(IPEndPoint endpoint)
    {
        if (_endpoints.TryGetValue(Key(endpoint), out var existing))
        {
            SendDatagram(CreatePacket(PacketType.ConnectAccepted, existing.Connection.Id, 0, RpcChannel.Reliable, []), endpoint);
            return;
        }
        var connection = new NetworkConnection(checked((ulong)Interlocked.Increment(ref _nextConnectionId)));
        var peer = new Peer(connection, endpoint);
        _peers[connection] = peer;
        _endpoints[Key(endpoint)] = peer;
        SendDatagram(CreatePacket(PacketType.ConnectAccepted, connection.Id, 0, RpcChannel.Reliable, []), endpoint);
        Connected?.Invoke(connection);
    }

    private void CompleteConnect(ulong id, IPEndPoint endpoint)
    {
        if (_connected.Task.IsCompleted) return;
        LocalConnection = new NetworkConnection(id);
        ServerConnection = new NetworkConnection(id);
        var peer = new Peer(ServerConnection, endpoint);
        _peers[ServerConnection] = peer;
        _endpoints[Key(endpoint)] = peer;
        _connected.TrySetResult(true);
        Connected?.Invoke(ServerConnection);
    }

    private async Task MaintenanceLoopAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
            while (await timer.WaitForNextTickAsync(_shutdown.Token))
            {
                var now = DateTime.UtcNow;
                foreach (var peer in _peers.Values)
                {
                    if (now - peer.LastSeenUtc > PeerTimeout) { DisconnectLocal(peer); continue; }
                    foreach (var entry in peer.Pending)
                        if (now - entry.Value.SentUtc >= ResendDelay && peer.Pending.TryUpdate(entry.Key, entry.Value with { SentUtc = now }, entry.Value))
                            SendDatagram(entry.Value.Data, peer.Endpoint);
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }

    private void DisconnectLocal(Peer peer)
    {
        if (!_peers.TryRemove(peer.Connection, out _)) return;
        _endpoints.TryRemove(Key(peer.Endpoint), out _);
        peer.Connection.IsConnected = false;
        Disconnected?.Invoke(peer.Connection);
    }

    private Task SendRawAsync(PacketType type, ulong id, uint sequence, RpcChannel channel, ReadOnlyMemory<byte> payload) =>
        _socket.SendAsync(CreatePacket(type, id, sequence, channel, payload.Span), _shutdown.Token).AsTask();

    private void SendDatagram(byte[] packet, IPEndPoint endpoint)
    {
        try
        {
            if (_server) _socket.Send(packet, endpoint);
            else _socket.Send(packet);
        }
        catch (SocketException) when (_shutdown.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested) { }
    }

    private static byte[] CreatePacket(PacketType type, ulong connectionId, uint sequence, RpcChannel channel, ReadOnlySpan<byte> payload)
    {
        var result = new byte[HeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(result, Magic);
        result[4] = (byte)type;
        result[5] = (byte)channel;
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(6), connectionId);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(14), sequence);
        payload.CopyTo(result.AsSpan(HeaderSize));
        return result;
    }

    private static string Key(IPEndPoint endpoint) => endpoint.ToString();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdown.Cancel();
        foreach (var peer in _peers.Values) DisconnectLocal(peer);
        _socket.Dispose();
        _shutdown.Dispose();
    }

    private enum PacketType : byte { Connect = 1, ConnectAccepted = 2, Data = 3, Ack = 4, Disconnect = 5 }
    private readonly record struct PendingPacket(byte[] Data, DateTime SentUtc);

    private sealed class Peer(NetworkConnection connection, IPEndPoint endpoint)
    {
        private readonly object _receiveLock = new();
        private readonly SortedDictionary<uint, byte[]> _reorderBuffer = [];
        private int _sequence;
        private uint _expectedSequence = 1;
        private long _rateWindow = Environment.TickCount64;
        private int _packetCount;
        public NetworkConnection Connection { get; } = connection;
        public IPEndPoint Endpoint { get; } = endpoint;
        public ConcurrentDictionary<uint, PendingPacket> Pending { get; } = new();
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
        public uint NextSequence() => unchecked((uint)Interlocked.Increment(ref _sequence));
        public IEnumerable<byte[]> Order(uint sequence, ReadOnlySpan<byte> payload)
        {
            lock (_receiveLock)
            {
                if (sequence < _expectedSequence || _reorderBuffer.ContainsKey(sequence)) return [];
                if (sequence - _expectedSequence > 1024) return [];
                _reorderBuffer[sequence] = payload.ToArray();
                var ready = new List<byte[]>();
                while (_reorderBuffer.Remove(_expectedSequence, out var packet))
                {
                    ready.Add(packet);
                    _expectedSequence++;
                }
                return ready;
            }
        }
        public bool AllowPacket()
        {
            var now = Environment.TickCount64;
            lock (_receiveLock)
            {
                if (now - _rateWindow >= 1000) { _rateWindow = now; _packetCount = 0; }
                return ++_packetCount <= 500;
            }
        }
    }
}
