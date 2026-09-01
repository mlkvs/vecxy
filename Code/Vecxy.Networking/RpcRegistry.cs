namespace Vecxy.Networking;

public delegate void RpcHandler(NetworkBehaviour behaviour, ReadOnlySpan<byte> payload, RpcContext context);

public sealed record RpcDescriptor(
    uint Id,
    RpcDirection Direction,
    RpcChannel Channel,
    bool RequireAuthority,
    RpcTarget Target,
    RpcHandler Handler);

public sealed class RpcRegistry
{
    private readonly Dictionary<uint, RpcDescriptor> _handlers = [];

    public void Register(RpcDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (_handlers.TryGetValue(descriptor.Id, out var existing))
        {
            if (existing.Direction == descriptor.Direction && existing.Handler.Method == descriptor.Handler.Method) return;
            throw new InvalidOperationException($"RPC ID collision at runtime: 0x{descriptor.Id:X8}.");
        }
        _handlers.Add(descriptor.Id, descriptor);
    }

    public bool TryGet(uint id, out RpcDescriptor? descriptor) => _handlers.TryGetValue(id, out descriptor);
}
