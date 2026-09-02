using System.Collections.Concurrent;
using Vecxy.Diagnostics;

namespace Vecxy.Networking;

public interface INetworking
{
    NetworkRole Role { get; }
    bool IsServer { get; }
    bool IsClient { get; }
    NetworkConnection? LocalConnection { get; }
    NetworkConnection? ServerConnection { get; }
    event Action<NetworkConnection>? Connected;
    event Action<NetworkConnection>? Disconnected;
    Task StartServerAsync(int port, CancellationToken cancellationToken = default);
    Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);
    void Configure(NetworkRole role, NetworkConnection? local = null, NetworkConnection? server = null);
    NetworkObject CreateObject(NetworkObjectId id, NetworkConnection? owner = null);
    bool TryGetObject(NetworkObjectId id, out NetworkObject? networkObject);
    void RegisterRpc(RpcDescriptor descriptor);
}

public sealed class NetworkRuntime : INetworking, IDisposable
{
    private INetworkTransport _transport;
    private readonly NetworkingOptions _options;
    private readonly RpcRegistry _registry;
    private readonly ConcurrentQueue<IncomingPacket> _incoming = new();
    private readonly ConcurrentQueue<(bool Connected, NetworkConnection Connection)> _connectionEvents = new();
    private readonly Dictionary<NetworkObjectId, NetworkObject> _objects = [];
    private bool _disposed;

    public NetworkRuntime(INetworkTransport transport, NetworkingOptions options, RpcRegistry registry)
    { _transport = transport; _options = options; _registry = registry; }

    public NetworkRole Role { get; private set; }
    public bool IsServer => Role is NetworkRole.Server or NetworkRole.Host;
    public bool IsClient => Role is NetworkRole.Client or NetworkRole.Host;
    public NetworkConnection? LocalConnection { get; private set; }
    public NetworkConnection? ServerConnection { get; private set; }
    public event Action<NetworkConnection>? Connected;
    public event Action<NetworkConnection>? Disconnected;

    public void Configure(NetworkRole role, NetworkConnection? local = null, NetworkConnection? server = null)
    { Role = role; LocalConnection = local; ServerConnection = server; }

    public async Task StartServerAsync(int port, CancellationToken cancellationToken = default)
    {
        var transport = await UdpNetworkTransport.StartServerAsync(port, cancellationToken);
        ReplaceTransport(transport);
        Configure(NetworkRole.Server);
    }

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        var transport = await UdpNetworkTransport.ConnectAsync(host, port, cancellationToken);
        ReplaceTransport(transport);
        Configure(NetworkRole.Client, transport.LocalConnection, transport.ServerConnection);
    }

    public NetworkObject CreateObject(NetworkObjectId id, NetworkConnection? owner = null)
    {
        if (id == NetworkObjectId.Empty) throw new ArgumentException("Network object ID cannot be empty.", nameof(id));
        var value = new NetworkObject(this, id, owner);
        if (!_objects.TryAdd(id, value)) throw new InvalidOperationException($"Network object {id} already exists.");
        return value;
    }

    public bool TryGetObject(NetworkObjectId id, out NetworkObject? networkObject) => _objects.TryGetValue(id, out networkObject);
    public void RegisterRpc(RpcDescriptor descriptor) => _registry.Register(descriptor);

    public void OnInitialize() => Subscribe(_transport);

    public void OnUpdate(float deltaTime)
    {
        while (_connectionEvents.TryDequeue(out var item))
            if (item.Connected) Connected?.Invoke(item.Connection); else Disconnected?.Invoke(item.Connection);
        while (_incoming.TryDequeue(out var incoming))
            using (incoming.Packet) Dispatch(incoming.Connection, incoming.Packet.ReadOnlySpan, incoming.Channel);
    }

    public void OnShutdown() => Unsubscribe(_transport);

    internal void SendServerRpc(NetworkBehaviour behaviour, uint methodId, ReadOnlySpan<byte> payload, RpcChannel channel)
    {
        if (IsServer) throw new InvalidOperationException("A locally executing ServerRpc must run its original body.");
        Send(ServerConnection ?? throw new InvalidOperationException("The client has no server connection."), behaviour, methodId, payload, channel);
    }

    internal void SendClientRpc(NetworkBehaviour behaviour, uint methodId, ReadOnlySpan<byte> payload, RpcChannel channel, RpcTarget target)
    {
        if (!IsServer) throw new InvalidOperationException("ClientRpc can only be sent by a server.");
        foreach (var connection in SelectTargets(behaviour.NetworkObject, target)) Send(connection, behaviour, methodId, payload, channel);
    }

    internal void SendTargetRpc(NetworkBehaviour behaviour, NetworkConnection target, uint methodId, ReadOnlySpan<byte> payload, RpcChannel channel)
    {
        if (!IsServer) throw new InvalidOperationException("TargetRpc can only be sent by a server.");
        Send(target, behaviour, methodId, payload, channel);
    }

    private void Send(NetworkConnection connection, NetworkBehaviour behaviour, uint methodId, ReadOnlySpan<byte> payload, RpcChannel channel)
    {
        if (payload.Length > _options.MaxRpcPayloadSize) throw new InvalidDataException("RPC payload exceeds MaxRpcPayloadSize.");
        using var packet = RpcProtocol.Write(behaviour.NetworkObject.Id, behaviour.BehaviourId, methodId, payload);
        _transport.Send(connection, packet.ReadOnlySpan, channel);
        if (_options.DetailedRpcLogging)
            Logger.Debug($"[Networking.Rpc] rpc=0x{methodId:X8} object={behaviour.NetworkObject.Id} behaviour={behaviour.BehaviourId} bytes={payload.Length}");
    }

    private void OnTransportReceived(NetworkReceive receive)
    {
        if (!receive.Connection.IsConnected) return;
        _incoming.Enqueue(new IncomingPacket(receive.Connection, PooledPacket.Copy(receive.Data.Span), receive.Channel));
    }

    private void ReplaceTransport(INetworkTransport replacement)
    {
        Unsubscribe(_transport);
        _transport.Dispose();
        _transport = replacement;
        Subscribe(_transport);
    }

    private void Subscribe(INetworkTransport transport)
    {
        transport.Received += OnTransportReceived;
        transport.Connected += OnConnected;
        transport.Disconnected += OnDisconnected;
    }

    private void Unsubscribe(INetworkTransport transport)
    {
        transport.Received -= OnTransportReceived;
        transport.Connected -= OnConnected;
        transport.Disconnected -= OnDisconnected;
    }

    private void OnConnected(NetworkConnection connection) => _connectionEvents.Enqueue((true, connection));
    private void OnDisconnected(NetworkConnection connection) => _connectionEvents.Enqueue((false, connection));

    private void Dispatch(NetworkConnection sender, ReadOnlySpan<byte> packet, RpcChannel channel)
    {
        if (!RpcProtocol.TryRead(packet, _options.MaxRpcPayloadSize, out var rpc)) { Violation(sender, "Malformed RPC packet."); return; }
        if (!_objects.TryGetValue(rpc.ObjectId, out var networkObject)) { Violation(sender, $"Unknown NetworkObject {rpc.ObjectId}."); return; }
        if (rpc.BehaviourId >= networkObject.Behaviours.Count) { Violation(sender, "Invalid BehaviourId."); return; }
        if (!_registry.TryGet(rpc.MethodId, out var descriptor) || descriptor is null) { Violation(sender, $"Unknown RPC 0x{rpc.MethodId:X8}."); return; }
        var expected = IsServer ? RpcDirection.Server : descriptor.Direction;
        if (descriptor.Direction != expected || descriptor.Channel != channel) { Violation(sender, "Wrong RPC direction or channel."); return; }
        if (IsServer && descriptor.RequireAuthority && networkObject.Owner != sender) { Violation(sender, "Unauthorized RPC."); return; }
        var behaviour = networkObject.Behaviours[rpc.BehaviourId];
        try
        {
            behaviour.BeginRpc(new RpcContext(sender, channel));
            descriptor.Handler(behaviour, rpc.Payload.Span, new RpcContext(sender, channel));
        }
        catch (Exception exception) { Violation(sender, $"RPC payload or handler failed: {exception.Message}"); }
        finally { behaviour.EndRpc(); }
    }

    private IEnumerable<NetworkConnection> SelectTargets(NetworkObject networkObject, RpcTarget target) => target switch
    {
        RpcTarget.All => _transport.Connections,
        RpcTarget.Owner => networkObject.Owner is null ? [] : [networkObject.Owner],
        RpcTarget.NonOwner => _transport.Connections.Where(x => x != networkObject.Owner),
        _ => networkObject.Observers
    };

    private void Violation(NetworkConnection connection, string message)
    {
        connection.ProtocolViolations++;
        Logger.Warning($"[Networking.Rpc.Security] {connection}: {message}");
        if (connection.ProtocolViolations >= _options.MaxProtocolViolations) _transport.Disconnect(connection, message);
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        while (_incoming.TryDequeue(out var packet)) packet.Packet.Dispose();
        _transport.Dispose();
    }

    private readonly record struct IncomingPacket(NetworkConnection Connection, PooledPacket Packet, RpcChannel Channel);
}
