using MemoryPack;

namespace Vecxy.Networking;

public enum RpcChannel : byte { Reliable, Unreliable }
public enum RpcTarget : byte { Observers, All, Owner, NonOwner }
public enum NetworkRole : byte { None, Server, Client, Host }
public enum NetworkMessageType : byte { Handshake = 1, Rpc = 2, StateDelta = 3, Spawn = 4, Despawn = 5 }
public enum RpcDirection : byte { Server, Client, Target }

[MemoryPackable]
public readonly partial record struct NetworkObjectId(ulong Value)
{
    public static readonly NetworkObjectId Empty = default;
    public override string ToString() => Value.ToString();
}

public sealed class NetworkConnection(ulong id)
{
    public ulong Id { get; } = id;
    public bool IsConnected { get; internal set; } = true;
    public int ProtocolViolations { get; internal set; }
    public override string ToString() => $"Connection#{Id}";
}

public readonly struct RpcContext(NetworkConnection sender, RpcChannel channel)
{
    public NetworkConnection Sender { get; } = sender;
    public RpcChannel Channel { get; } = channel;
}

public sealed record NetworkingOptions
{
    public int MaxRpcPayloadSize { get; init; } = 64 * 1024;
    public int MaxProtocolViolations { get; init; } = 5;
    public bool DetailedRpcLogging { get; init; }
    public ulong ProtocolFingerprint { get; init; }
}
