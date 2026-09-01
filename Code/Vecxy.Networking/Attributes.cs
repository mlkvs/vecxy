namespace Vecxy.Networking;

[AttributeUsage(AttributeTargets.Method)]
public sealed class ServerRpcAttribute : Attribute
{
    public bool RequireAuthority { get; init; } = true;
    public RpcChannel Channel { get; init; } = RpcChannel.Reliable;
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class ClientRpcAttribute : Attribute
{
    public RpcChannel Channel { get; init; } = RpcChannel.Reliable;
    public RpcTarget Target { get; init; } = RpcTarget.Observers;
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class TargetRpcAttribute : Attribute
{
    public RpcChannel Channel { get; init; } = RpcChannel.Reliable;
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class NetworkedAttribute : Attribute
{
    public string? OnChanged { get; init; }
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ServerOnlyAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ClientOnlyAttribute : Attribute;

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class VecxyNetworkingWeavedAttribute : Attribute;
