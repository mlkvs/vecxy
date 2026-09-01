namespace Vecxy.Networking;

public readonly record struct NetworkReceive(
    NetworkConnection Connection,
    ReadOnlyMemory<byte> Data,
    RpcChannel Channel);

public interface INetworkTransport : IDisposable
{
    event Action<NetworkReceive>? Received;
    event Action<NetworkConnection>? Connected;
    event Action<NetworkConnection>? Disconnected;
    IReadOnlyCollection<NetworkConnection> Connections { get; }
    void Send(NetworkConnection connection, ReadOnlySpan<byte> data, RpcChannel channel);
    void Disconnect(NetworkConnection connection, string reason);
}

public sealed class NullNetworkTransport : INetworkTransport
{
    public event Action<NetworkReceive>? Received { add { } remove { } }
    public event Action<NetworkConnection>? Connected { add { } remove { } }
    public event Action<NetworkConnection>? Disconnected { add { } remove { } }
    public IReadOnlyCollection<NetworkConnection> Connections => Array.Empty<NetworkConnection>();
    public void Send(NetworkConnection connection, ReadOnlySpan<byte> data, RpcChannel channel) { }
    public void Disconnect(NetworkConnection connection, string reason) => connection.IsConnected = false;
    public void Dispose() { }
}
