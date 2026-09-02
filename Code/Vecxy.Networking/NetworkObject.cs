namespace Vecxy.Networking;

public abstract class NetworkBehaviour
{
    private RpcContext? _currentRpcContext;
    public NetworkObject NetworkObject { get; private set; } = null!;
    public byte BehaviourId { get; private set; }
    public bool IsServer => NetworkObject.Runtime.IsServer && !NetworkObject.Runtime.IsExecutingLocalClientRpc;
    public bool IsClient => NetworkObject.Runtime.IsClient;
    public bool IsOwner => NetworkObject.Owner == NetworkObject.Runtime.LocalConnection;
    public NetworkConnection? Owner => NetworkObject.Owner;
    protected NetworkConnection RpcSender => _currentRpcContext?.Sender ??
        throw new InvalidOperationException("RpcSender is only available while executing an incoming ServerRpc.");
    protected RpcChannel RpcChannel => _currentRpcContext?.Channel ??
        throw new InvalidOperationException("RpcChannel is only available while executing an incoming RPC.");

    internal void Attach(NetworkObject networkObject, byte behaviourId)
    {
        if (NetworkObject is not null) throw new InvalidOperationException("NetworkBehaviour is already attached.");
        NetworkObject = networkObject;
        BehaviourId = behaviourId;
    }

    internal void BeginRpc(RpcContext context) => _currentRpcContext = context;
    internal void EndRpc() => _currentRpcContext = null;

    protected void SendServerRpc(uint rpcMethodId, ReadOnlySpan<byte> payload, RpcChannel channel) =>
        NetworkObject.Runtime.SendServerRpc(this, rpcMethodId, payload, channel);
    protected void SendServerRpc(uint rpcMethodId, byte[] payload, RpcChannel channel) =>
        NetworkObject.Runtime.SendServerRpc(this, rpcMethodId, payload, channel);
    protected void SendClientRpc(uint rpcMethodId, ReadOnlySpan<byte> payload, RpcChannel channel, RpcTarget target) =>
        NetworkObject.Runtime.SendClientRpc(this, rpcMethodId, payload, channel, target);
    protected void SendClientRpc(uint rpcMethodId, byte[] payload, RpcChannel channel, RpcTarget target) =>
        NetworkObject.Runtime.SendClientRpc(this, rpcMethodId, payload, channel, target);
    protected void SendTargetRpc(NetworkConnection target, uint rpcMethodId, ReadOnlySpan<byte> payload, RpcChannel channel) =>
        NetworkObject.Runtime.SendTargetRpc(this, target, rpcMethodId, payload, channel);
    protected void SendTargetRpc(NetworkConnection target, uint rpcMethodId, byte[] payload, RpcChannel channel) =>
        NetworkObject.Runtime.SendTargetRpc(this, target, rpcMethodId, payload, channel);
    protected void MarkNetworkedDirty(int memberIndex) => NetworkObject.MarkDirty(BehaviourId, memberIndex);

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public virtual void __RegisterRpcs(INetworking networking) { }
}

public sealed class NetworkObject
{
    private readonly List<NetworkBehaviour> _behaviours = [];
    private readonly HashSet<NetworkConnection> _observers = [];
    private readonly Dictionary<byte, System.Collections.BitArray> _dirty = [];

    internal NetworkObject(NetworkRuntime runtime, NetworkObjectId id, NetworkConnection? owner)
    { Runtime = runtime; Id = id; Owner = owner; }

    public NetworkRuntime Runtime { get; }
    public NetworkObjectId Id { get; }
    public NetworkConnection? Owner { get; internal set; }
    public IReadOnlyList<NetworkBehaviour> Behaviours => _behaviours;
    public IReadOnlyCollection<NetworkConnection> Observers => _observers;

    public void AddBehaviour(NetworkBehaviour behaviour)
    {
        ArgumentNullException.ThrowIfNull(behaviour);
        if (_behaviours.Count >= byte.MaxValue) throw new InvalidOperationException("A NetworkObject supports at most 255 behaviours.");
        behaviour.Attach(this, checked((byte)_behaviours.Count));
        _behaviours.Add(behaviour);
        behaviour.__RegisterRpcs(Runtime);
    }

    public void AddObserver(NetworkConnection connection) => _observers.Add(connection);
    public void RemoveObserver(NetworkConnection connection) => _observers.Remove(connection);

    internal void MarkDirty(byte behaviourId, int memberIndex)
    {
        if (!Runtime.IsServer || memberIndex < 0) return;
        if (!_dirty.TryGetValue(behaviourId, out var bits)) _dirty[behaviourId] = bits = new(memberIndex + 1);
        if (bits.Length <= memberIndex) bits.Length = memberIndex + 1;
        bits[memberIndex] = true;
    }
}
